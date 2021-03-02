using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

// reworked

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

        var calculateDirectionJob = new CalculateDirectionJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            targetHandle = GetComponentTypeHandle<TargetPosition>() ,
            directionHandle = GetComponentTypeHandle<Direction>()
        };
        handle = calculateDirectionJob.ScheduleParallel( query , 1 , Dependency );

        var updateMovingJob = new UpdateMovementStateJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            targetHandle = GetComponentTypeHandle<TargetPosition>() ,
            movingHandle = GetComponentTypeHandle<Moving>()
        };
        handle = updateMovingJob.ScheduleParallel( query , 1 , handle );

        var applyMoveForceJob = new ApplyMoveForceJob
        {
            forceHandle = GetComponentTypeHandle<MoveForce>() ,
            directionHandle = GetComponentTypeHandle<Direction>() ,
            movingHandle = GetComponentTypeHandle<Moving>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>()
        };
        handle = applyMoveForceJob.ScheduleParallel( query , 1 , handle );

        var applyDragJob = new ApplyDragJob
        {
            dragHandle = GetComponentTypeHandle<Drag>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>()
        };
        handle = applyDragJob.ScheduleParallel( query , 1 , handle );

        var applyVelocityJob = new ApplyVelocityJob
        {
            dt = Time.DeltaTime ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
        };
        handle = applyVelocityJob.ScheduleParallel( query , 1 , handle );

        var boundsJob = new KeepWithinBoundsJob
        {
            translationHandle = GetComponentTypeHandle<Translation>()
        };
        handle = boundsJob.ScheduleParallel( query , 1 , handle );

        Dependency = handle;
    }

    [BurstCompile]
    private struct CalculateDirectionJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<TargetPosition> targetHandle;
        public ComponentTypeHandle<Direction> directionHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<TargetPosition> batchTarget = batchInChunk.GetNativeArray( targetHandle );
            NativeArray<Direction> batchDirection = batchInChunk.GetNativeArray( directionHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float distance = math.distance( batchTarget[ i ].Value , batchTranslation[ i ].Value );
                float3 direction = batchTarget[ i ].Value - batchTranslation[ i ].Value;
                float3 directionNormalized = direction / distance;
                batchDirection[ i ] = new Direction { Value = directionNormalized };
            }
        }
    }
    [BurstCompile]
    private struct UpdateMovementStateJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<TargetPosition> targetHandle;
        public ComponentTypeHandle<Moving> movingHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<TargetPosition> batchTarget = batchInChunk.GetNativeArray( targetHandle );
            NativeArray<Moving> batchMoving = batchInChunk.GetNativeArray( movingHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float distance = math.distance( batchTarget[ i ].Value , batchTranslation[ i ].Value );
                int moving = math.select( 1 , -1 , distance <= 0.05f );
                batchMoving[ i ] = new Moving { Value = moving };
            }
        }
    }
    [BurstCompile]
    private struct ApplyMoveForceJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<MoveForce> forceHandle;
        [ReadOnly] public ComponentTypeHandle<Direction> directionHandle;
        [ReadOnly] public ComponentTypeHandle<Moving> movingHandle;
        public ComponentTypeHandle<Velocity> velocityHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<MoveForce> batchForce = batchInChunk.GetNativeArray( forceHandle );
            NativeArray<Direction> batchDirection = batchInChunk.GetNativeArray( directionHandle );
            NativeArray<Moving> batchMoving = batchInChunk.GetNativeArray( movingHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                if ( batchMoving[ i ].Value == 1 )
                {
                    float3 newVelocity = batchVelocity[ i ].Value + batchForce[ i ].Value * batchDirection[ i ].Value;
                    batchVelocity[ i ] = new Velocity { Value = newVelocity };
                }
            }
        }
    }
    [BurstCompile]
    private struct ApplyDragJob : IJobEntityBatch
    {
        [ReadOnly] public ComponentTypeHandle<Drag> dragHandle;
        public ComponentTypeHandle<Velocity> velocityHandle;

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
    [BurstCompile]
    private struct ApplyVelocityJob : IJobEntityBatch
    {
        [ReadOnly] public float dt;
        [ReadOnly] public ComponentTypeHandle<Velocity> velocityHandle;
        public ComponentTypeHandle<Translation> translationHandle;

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
    [BurstCompile]
    private struct KeepWithinBoundsJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Translation> translationHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float newX = batchTranslation[ i ].Value.x;
                float newZ = batchTranslation[ i ].Value.z;

                if ( newX <= 20 )
                {
                    newX = 21;
                }
                if ( newZ <= 20 )
                {
                    newZ = 21;
                }

                batchTranslation[ i ] = new Translation { Value = new float3( newX , batchTranslation[ i ].Value.y , newZ ) };
            }
        }
    }
}
