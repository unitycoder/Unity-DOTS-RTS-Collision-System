# Unity-DOTS-RTS-Collision-System
A basic RTS collision system.
Unity 2020.2.3f1

A physics collision system to handle very large amounts of units, in the 30-50 thousand range.
Uses ECS, jobs and Burst.
It uses a fixed grid to spatially partition the units.
The grid is state-dependant, meaning if you kill units you must rebuild it occasionally;
this shortcut allows my broadphase to be much faster.
You can get away with updating the grid every 5 minutes, so no big deal.


UnitDynamicCollisionSystemGrid is the main file, the others are just scripts to setup a very basic scene, sorry about the mess lol.
If the scene has some broken dependencies, all you have to do is add a mesh and material to GameManager in the editor,
and it should work.
You can change the number of units and where they spawn in UnitSpawner()

You can control the camera in Play-Mode with WASD, TG, and mouse.
You can select units with left click, or drag select, and right click to send them to location
By default, all units have random target position
You can disable this in unit spawner
You can change number of units spanwed and where they spawn in unit spawner


I chose a grid to represent spatial partitioning because you just can't beat the performance of the grid
Obviously RAM is sacrificed, for example a 6kmx6km map would be 288mb in this example, but I don't really care
because people have so much ram today that it doesn't really matter, and the performance benefit is so sweet.

I have tried quadtree, and spatial hashing, and those use much less memory, but they just bottleneck above
30k units on my machine, and in my case I want to push 50k units.

Next updates include:
- hierarchical grid, for different unit sizes
- k-d or binary tree for static collision
- SIMD some stuff