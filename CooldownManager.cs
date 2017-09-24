using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CooldownManager {

    /// <summary>
    /// A helper class that allows easy implementation of performing actions
    /// on a given maximum interval with cooldown support.
    /// The manager supports cooldown tracking for multiple objects or events through
    /// the use of a lookup table and string ids.
    /// The built-in cooldown or falloff factor is increased at each breached action interval,
    /// up to the maximum specified.
    /// Per default, the falloff table looks like this:
    ///     #1: 0h 0m 5s
    ///     #1.5: 0h 0m 11s
    ///     #2: 0h 0m 25s
    ///     #2.5: 0h 0m 55s
    ///     #3: 0h 2m 125s
    ///     #3.5: 0h 4m 279s
    ///     #4: 0h 10m 625s
    ///     #4.5: 0h 23m 1397s
    ///     #5: 0h 52m 3125s
    /// Note: this class is thread-safe.
    /// </summary>
    public class CooldownManager {

        #region Helper Status Class CooldownState
        private class CooldownState {

            public DateTime Timestamp;
            public int CooldownHits;
            public double FalloffFactor;

            public CooldownState() {
                this.Timestamp = DateTime.UtcNow;
                this.FalloffFactor = 1;
                this.CooldownHits = 0;
            }
        }
        #endregion


        /// <summary>
        /// The lock for thread safe operations.
        /// </summary>
        private static object _cooldownLock = new object();

        /// <summary>
        /// The internal lookup table for the different action events.
        /// </summary>
        private static Dictionary<string,CooldownState> _cooldownStates = new Dictionary<string,CooldownState>();

        /// <summary>
        /// The minimum action interval in seconds.
        /// A action will never be performed sequentially in less that this specified interval.
        /// </summary>
        public int MinActionIntervalSeconds = 5;

        /// <summary>
        /// The maximum falloff factor.
        /// </summary>
        public double MaxFalloffFactor = 5;

        /// <summary>
        /// The falloff factor step or increment. This can be less than 1, or zero if no falloff is wished.
        /// </summary>
        public double FalloffFactorStep = 0.5;

        private void _log(string str) {
        }

        /// <summary>
        /// Logs all intervals up to the max specified.
        /// </summary>
        public void LogAllIntervals() {
            _log("Falloff intervals: ");
            for(double f = 1; f <= MaxFalloffFactor; f+=this.FalloffFactorStep) {
                var ts = new TimeSpan(0, 0, (int)Math.Pow(MinActionIntervalSeconds, f));
                _log( "  #"+f+": "+(int)ts.TotalHours + "h " +(int)ts.TotalMinutes + "m " +(int)ts.TotalSeconds + "s" );
                if(this.FalloffFactorStep == 0) break;
            }
        }

        /// <summary>
        /// Removes the old states to ensure that the state tracking doesnt grow too large.
        /// </summary>
        public void RemoveOldStates() {
            lock(_cooldownLock) {
                foreach(var key in _cooldownStates.Keys.ToList()) {
                    var state = _cooldownStates[key];
                    var minActionTimespan = new TimeSpan(0, 0, (int)Math.Ceiling(Math.Pow(this.MinActionIntervalSeconds, state.FalloffFactor)) * 2);
                    if(state.Timestamp < DateTime.UtcNow - minActionTimespan) {
                        _log("Removing " + key);
                        _cooldownStates.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Performs the action with cooldown for the given id or hash.
        /// For example: 
        ///     var cdm = new CooldownManager();
        ///     cdm.PerformActionWithCooldown(error.Message, () => {
        /// 	    CreateErrorTicket();
        ///     });
        /// </summary>
        /// <param name="idOrHash">The identifier or hash.</param>
        /// <param name="action">The action.</param>
        /// <param name="force">if set to <c>true</c> [force].</param>
        public void PerformActionWithCooldown(string idOrHash, Action action, bool force = false) {
            // Force the action?
            if(force == true) {
                action();
            } else {

                // House keeping...
                RemoveOldStates();

                // Get the state
                CooldownState state = null;
                lock(_cooldownLock) {
                    if(!_cooldownStates.ContainsKey(idOrHash)) {
                        // New
                        state = new CooldownState();
                        _cooldownStates.Add(idOrHash, state);
                    } else {
                        // Exists
                        state = _cooldownStates[idOrHash];
                    }
                }

                // Cooldown?
                var minActionTimespan = new TimeSpan(0,0, (int)Math.Pow(this.MinActionIntervalSeconds,state.FalloffFactor));
                //_log("ts=" + minActionTimespan.TotalSeconds+"   -- is="+this.MinActionIntervalSeconds+"   -- ff="+ state.FalloffFactor+" s="+Math.Pow(this.MinActionIntervalSeconds,state.FalloffFactor));
                if(DateTime.UtcNow < state.Timestamp + minActionTimespan) {

                    // Cool down
                    state.CooldownHits++;
                    //_log("cooldown f="+state.FalloffFactor+" h="+state.CooldownHits + " ts="+minActionTimespan.TotalSeconds);

                } else {

                    if(state.CooldownHits > 0) state.FalloffFactor += this.FalloffFactorStep;
                    else state.FalloffFactor -= this.FalloffFactorStep;
                    if(state.FalloffFactor < 1) state.FalloffFactor = 1;
                    if(state.FalloffFactor > this.MaxFalloffFactor) state.FalloffFactor = this.MaxFalloffFactor;

                    state.Timestamp = DateTime.UtcNow;
                    state.CooldownHits = 0;

                    // Do action
                    action();
                    //_log("action f="+state.FalloffFactor);


                }

            }
        }

        /// <summary>
        /// Shortcut utility for calling the cooldown on a error by automatically
        /// converting the error tree (error + all inner exceptions) to a id.
        /// </summary>
        /// <param name="error">The error.</param>
        /// <param name="action">The action.</param>
        /// <param name="force">if set to <c>true</c> [force].</param>
        public void PerformActionWithCooldown(Exception error, Action action, bool force = false) {
            // Get the id by crawling the exception tree
            string id = "error";
            var e = error;
            while(e != null) {
                id += "_"+e.GetType().Name+":"+e.Message;
                e = e.InnerException;
            }
            // Pass on...
            _log(id);
            PerformActionWithCooldown(id, action, force);
        }

    }
}

