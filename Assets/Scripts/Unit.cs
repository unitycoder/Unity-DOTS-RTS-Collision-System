using System;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Mathematics;

public struct UnitTag : IComponentData
{

}

public struct UnitMoving : IComponentData
{
    public int Value;
}
public struct MoveSpeed : IComponentData
{
    public float Walk;
    public float Run;
}
public struct TargetPosition : IComponentData
{
    public float3 Value;
}
public struct Velocity : IComponentData
{
    public float3 Value;
}
public struct Mass : IComponentData
{
    public float Value;
}
public struct CollisionCell : IComponentData
{
    public int Value;
}

// an example of a compressed physics representation for more performance, not used in this example
public struct UnitPhysicsData : IComponentData
{
    public ushort xp;
    public ushort yp;
    public ushort zp;
    public byte xv;
    public byte yv;
    public byte zv;
    public ushort m;
}
