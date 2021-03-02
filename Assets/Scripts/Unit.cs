using System;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Mathematics;

// same

public struct UnitTag : IComponentData
{

}

public struct Moving : IComponentData
{
    public int Value;
}
public struct Selected : IComponentData
{
    public bool Value;
}
public struct Direction : IComponentData
{
    public float3 Value;
}
public struct TargetPosition : IComponentData
{
    public float3 Value;
}
public struct MoveForce : IComponentData
{
    public float Value;
}
public struct Velocity : IComponentData
{
    public float3 Value;
}
public struct Drag : IComponentData
{
    public float Value;
}
public struct Mass : IComponentData
{
    public float Value;
}
public struct CollisionCell : IComponentData
{
    public int Value;
}
public struct CollisionCellMulti : IComponentData
{
    public int bL;
    public int bR;
    public int tL;
    public int tR;
}