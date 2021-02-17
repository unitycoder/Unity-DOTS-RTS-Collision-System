using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

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
        UpdateVelocityJob job1 = new UpdateVelocityJob();
        job1.translationHandle = GetComponentTypeHandle<Translation>( true );
        job1.targetHandle = GetComponentTypeHandle<TargetPosition>( true );
        job1.speedHandle = GetComponentTypeHandle<MoveSpeed>( true );
        job1.velocityHandle = GetComponentTypeHandle<Velocity>( false );
        Dependency = job1.ScheduleParallel( query , 1 , Dependency );

        MoveUnitsJob job2 = new MoveUnitsJob();
        job2.dt = Time.DeltaTime;
        job2.velocityHandle = GetComponentTypeHandle<Velocity>( true );
        job2.translationHandle = GetComponentTypeHandle<Translation>( false );
        Dependency = job2.ScheduleParallel( query , 1 , Dependency );
    }

    private struct MoveUnitsJob : IJobEntityBatch
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
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<TargetPosition> targetHandle;
        [ReadOnly] public ComponentTypeHandle<MoveSpeed> speedHandle;
        public ComponentTypeHandle<Velocity> velocityHandle;

        [BurstCompile]
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<TargetPosition> batchTarget = batchInChunk.GetNativeArray( targetHandle );
            NativeArray<MoveSpeed> batchSpeed = batchInChunk.GetNativeArray( speedHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 direction = batchTarget[ i ].Value - batchTranslation[ i ].Value;
                float3 velocity = math.normalizesafe( direction ) * batchSpeed[ i ].Walk;
                batchVelocity[ i ] = new Velocity { Value = velocity };
            }
        }
    }
}
