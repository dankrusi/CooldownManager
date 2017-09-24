# WorkerPool
A helper class that allows easy implementation of performing actions
on a given maximum interval with cooldown support.
The manager supports cooldown tracking for multiple objects or events through
the use of a lookup table and string ids.
The built-in cooldown or falloff factor is increased at each breached action interval,
up to the maximum specified.
