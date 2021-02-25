using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class UnitMovementSystem : SystemBase
{
    private EntityQuery query;

    protected override void OnCreate()
    {
        base.OnCreate();
        var description = new EntityQueryDesc()
        {
            All = new ComponentType[] {
                ComponentType.ReadWrite<Translation>(),
                ComponentType.ReadOnly<Velocity>() }
        };
        query = GetEntityQuery( description );
    }

    protected override void OnUpdate()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        JobHandle handle = new JobHandle();

        if ( Input.GetKeyDown( KeyCode.Space ) )
        {
            CalculateDirectionJob calculateDirectionJob = new CalculateDirectionJob
            {
                translationHandle = GetComponentTypeHandle<Translation>() ,
                targetHandle = GetComponentTypeHandle<TargetPosition>() ,
                directionHandle = GetComponentTypeHandle<Direction>()
            };

            handle = calculateDirectionJob.ScheduleParallel( query , 1 , Dependency );
        }

        ApplyMoveForceJob applyMoveForceJob = new ApplyMoveForceJob
        {
            forceHandle = GetComponentTypeHandle<MoveForce>() ,
            directionHandle = GetComponentTypeHandle<Direction>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>()
        };

        handle = applyMoveForceJob.ScheduleParallel( query , 1 , handle );

        ApplyDragJob applyDragJob = new ApplyDragJob
        {
            dragHandle = GetComponentTypeHandle<Drag>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>()
        };

        handle = applyDragJob.ScheduleParallel( query , 1 , handle );

        ApplyVelocityJob applyVelocityJob = new ApplyVelocityJob
        {
            dt = Time.DeltaTime ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
        };

        handle = applyVelocityJob.ScheduleParallel( query , 1 , handle );

        Dependency = handle;
    }

    private struct UpdateTranslationJob : IJobEntityBatch
    {
        [ReadOnly] public float dt;
        [ReadOnly] public ComponentTypeHandle<Velocity> velocityHandle;
        public ComponentTypeHandle<Translation> translationHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 newPosition = batchTranslation[ i ].Value + ( batchVelocity[ i ].Value * dt );
                batchTranslation[ i ] = new Translation { Value = newPosition };
            }
        }
    }
    private struct UpdateVelocityJob : IJobEntityBatch
    {
        [ReadOnly] public float DRAG;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<TargetPosition> targetHandle;
        [ReadOnly] public ComponentTypeHandle<MoveForce> speedHandle;
        public ComponentTypeHandle<Velocity> velocityHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<TargetPosition> batchTarget = batchInChunk.GetNativeArray( targetHandle );
            NativeArray<MoveForce> batchMoveForce = batchInChunk.GetNativeArray( speedHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                // a = f/m
                // v += a
                // v -= d
                // p += v

                float px = batchTranslation[ i ].Value.x;
                float pz = batchTranslation[ i ].Value.z;
                float a = batchMoveForce[ i ].Value;


                float3 direction = batchTarget[ i ].Value - batchTranslation[ i ].Value;
                float3 velocity = math.normalizesafe( direction ) * ( batchMoveForce[ i ].Value );
                
                batchVelocity[ i ] = new Velocity { Value = velocity };
            }
        }
    }
    
    private struct CalculateDirectionJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<TargetPosition> targetHandle;
        public ComponentTypeHandle<Direction> directionHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<TargetPosition> batchTarget = batchInChunk.GetNativeArray( targetHandle );
            NativeArray<Direction> batchDirection = batchInChunk.GetNativeArray( directionHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 direction = batchTarget[ i ].Value - batchTranslation[ i ].Value;
                batchDirection[ i ] = new Direction { Value = direction };
            }
        }
    }
    private struct ApplyMoveForceJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<MoveForce> forceHandle;
        [ReadOnly] public ComponentTypeHandle<Direction> directionHandle;
        public ComponentTypeHandle<Velocity> velocityHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<MoveForce> batchForce = batchInChunk.GetNativeArray( forceHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );
            NativeArray<Direction> batchDirection = batchInChunk.GetNativeArray( directionHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 newVelocity = batchVelocity[ i ].Value + batchForce[ i ].Value * batchDirection[i].Value;
                batchVelocity[ i ] = new Velocity { Value = newVelocity };
            }
        }
    }
    private struct ApplyDragJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<Drag> dragHandle;
        public ComponentTypeHandle<Velocity> velocityHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Drag> batchDrag = batchInChunk.GetNativeArray( dragHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 drag = batchVelocity[ i ].Value / batchDrag[ i ].Value;
                float3 newVelocity = batchVelocity[ i ].Value - drag;
                batchVelocity[ i ] = new Velocity { Value = newVelocity };
            }
        }
    }
    private struct ApplyVelocityJob : IJobEntityBatch
    {
        [ReadOnly] public float dt;
        [ReadOnly] public ComponentTypeHandle<Velocity> velocityHandle;
        public ComponentTypeHandle<Translation> translationHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 newTranslation = batchTranslation[ i ].Value + batchVelocity[ i ].Value * dt;
                batchTranslation[ i ] = new Translation { Value = newTranslation };
            }
        }
    }
}
