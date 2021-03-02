using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// reworked

// CAPITOL VARIABLE NAMES WITH _ ARE CONSTANTS
public class UnitDynamicCollisionSystemGrid : SystemBase
{
    private NativeArray<ushort> grid;
    private NativeQueue<UpdateCellData> cellsToUpdate;

    private const float CELL_SIZE = 1f;
    private const int CELLS_ACROSS = 3000;
    private const int CELL_CAPACITY = 6;
    private const ushort VOID_CELL_VALUE = 60000;

    private EntityQuery query;

    protected unsafe override void OnStartRunning()
    {
        base.OnStartRunning();
        Setup();
    }
    protected override void OnUpdate()
    {
        CollisionSystem();
    }
    protected override void OnDestroy()
    {
        Cleanup();
        base.OnDestroy();
    }

    private void Setup()
    {
        grid = new NativeArray<ushort>( CELLS_ACROSS * CELLS_ACROSS * CELL_CAPACITY , Allocator.Persistent , NativeArrayOptions.UninitializedMemory );
        cellsToUpdate = new NativeQueue<UpdateCellData>( Allocator.Persistent );
        query = GetEntityQuery( typeof( UnitTag ) );

        InitializeGridJob initJob = new InitializeGridJob // set all grid values to void value
        {
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            spatialGrid = grid ,
        };
        JobHandle handle = initJob.Schedule( Dependency );
        handle.Complete();
        BuildGridJob buildGridJob = new BuildGridJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCell>() ,
            grid = grid ,
        };
        handle = buildGridJob.Schedule( query , handle );
        handle.Complete();
        Dependency = handle;
    }
    private void Cleanup()
    {
        grid.Dispose();
        cellsToUpdate.Dispose();
        base.OnDestroy();
    }
    private void CollisionSystem()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        int numUnits = query.CalculateEntityCount();

        var copyPositions   = new NativeArray<float3>( numUnits , Allocator.TempJob , NativeArrayOptions.UninitializedMemory );
        var copyVelocities  = new NativeArray<float3>( numUnits , Allocator.TempJob , NativeArrayOptions.UninitializedMemory );
        var copyMass        = new NativeArray<byte>( numUnits , Allocator.TempJob , NativeArrayOptions.UninitializedMemory );
        var collisionFlags  = new NativeBitArray( numUnits , Allocator.TempJob , NativeArrayOptions.ClearMemory );
        var collisionPairs  = new NativeQueue<CollidingPair>( Allocator.TempJob );

        var updateUnitCells = new UpdateUnitCellsJob // Update the cell the unit is currently in and queue changed cells
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            unitsToUpdate = cellsToUpdate.AsParallelWriter() ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCell>()
        };
        JobHandle handle = updateUnitCells.ScheduleParallel( query , 1 , Dependency );

        var updateGridCells = new UpdateGridCellsJob // remove units from old cells and add to new ones
        {
            CELL_CAPACITY = CELL_CAPACITY ,
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            grid = grid ,
            cellData = cellsToUpdate
        };
        handle = updateGridCells.Schedule( handle );

        var copyUnitPosition = new CopyPositionsJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            copyArray = copyPositions
        };
        var copyUnitVelocity = new CopyVelocitesJob
        {
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            copyArray = copyVelocities
        };
        var copyUnitMass = new CopyMassJob
        {
            massHandle = GetComponentTypeHandle<Mass>() ,
            copyArray = copyMass
        };
        handle = JobHandle.CombineDependencies(
            copyUnitPosition.ScheduleParallel( query , 1 , handle ) ,
            copyUnitVelocity.ScheduleParallel( query , 1 , handle ) ,
            copyUnitMass.ScheduleParallel( query , 1 , handle ) );

        var findCollidingPairs = new FindCollidingPairsJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            RADIUS = 0.5f ,
            grid = grid ,
            copyPositions = copyPositions ,
            collidingPairs = collisionPairs.AsParallelWriter() ,
            collisionFlags = collisionFlags ,
        };
        handle = findCollidingPairs.Schedule( copyPositions.Length , 128 , handle );
        handle.Complete();

        var foundPairs = collisionPairs.ToArray( Allocator.TempJob );

        var resolveCollisionsJob = new ResolveCollisionsJob()
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            RADIUS = 0.5f ,
            collisionPairs = foundPairs ,
            copyPositions = copyPositions ,
            copyVelocities = copyVelocities ,
            copyMass = copyMass
        };
        handle = resolveCollisionsJob.Schedule( foundPairs.Length , 128 , handle );

        var writeResultsToUnits = new WriteDataJob
        {
            copyPositions = copyPositions ,
            copyVelocities = copyVelocities ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>()
        };
        handle = writeResultsToUnits.ScheduleParallel( query , 1 , handle );

        var disposeHandle = copyPositions.Dispose( handle );
        disposeHandle = JobHandle.CombineDependencies( disposeHandle , copyVelocities.Dispose( handle ) );
        disposeHandle = JobHandle.CombineDependencies( disposeHandle , copyMass.Dispose( handle ) );
        disposeHandle = JobHandle.CombineDependencies( disposeHandle , collisionFlags.Dispose( handle ) );
        disposeHandle = JobHandle.CombineDependencies( disposeHandle , collisionPairs.Dispose( handle ) );
        disposeHandle = JobHandle.CombineDependencies( disposeHandle , foundPairs.Dispose( handle ) );

        Dependency = disposeHandle;
    }

    [BurstCompile]
    private unsafe struct InitializeGridJob : IJob
    {
        [ReadOnly] public ushort VOID_CELL_VALUE;
        public NativeArray<ushort> spatialGrid;

        public void Execute()
        {
            ushort value = VOID_CELL_VALUE;
            void* p = &value;
            UnsafeUtility.MemCpyReplicate( spatialGrid.GetUnsafePtr() , p , sizeof( ushort ) , spatialGrid.Length );
        }
    }
    [BurstCompile]
    private struct BuildGridJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public ushort VOID_CELL_VALUE;

        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        public ComponentTypeHandle<CollisionCell> cellHandle;
        public NativeArray<ushort> grid;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCell> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float pz = batchTranslation[ i ].Value.z;
                int cell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( pz / CELL_SIZE ) * CELLS_ACROSS );
                int gridIndex = cell * CELL_CAPACITY;

                batchCell[ i ] = new CollisionCell { Value = cell };

                for ( int j = 0; j < CELL_CAPACITY; j++ )
                {
                    if ( grid[ gridIndex + j ] == VOID_CELL_VALUE )
                    {
                        grid[ gridIndex + j ] = ( ushort ) ( indexOfFirstEntityInQuery + i );
                        break;
                    }
                }
            }
        }
    }
    [BurstCompile]
    private struct UpdateUnitCellsJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        public ComponentTypeHandle<CollisionCell> cellHandle;
        public NativeQueue<UpdateCellData>.ParallelWriter unitsToUpdate;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCell> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float pz = batchTranslation[ i ].Value.z;
                int newCell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( pz / CELL_SIZE ) * CELLS_ACROSS );
                int oldCell = batchCell[ i ].Value;

                batchCell[ i ] = new CollisionCell { Value = newCell };

                if ( oldCell != newCell )
                {
                    UpdateCellData data = new UpdateCellData
                    {
                        unitID = indexOfFirstEntityInQuery + i ,
                        oldCell = oldCell ,
                        newCell = newCell
                    };

                    unitsToUpdate.Enqueue( data );
                }
            }
        }
    }
    [BurstCompile]
    private struct UpdateGridCellsJob : IJob
    {
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public ushort VOID_CELL_VALUE;

        public NativeArray<ushort> grid;
        public NativeQueue<UpdateCellData> cellData;

        public void Execute()
        {
            // this looks slow but because units rarely change cells every frame the job time is very small
            while ( cellData.TryDequeue( out UpdateCellData data ) )
            {
                int gridIndex = data.oldCell * CELL_CAPACITY;

                for ( int i = 0; i < CELL_CAPACITY; i++ )
                {
                    // basically loop through the cell until id is found
                    // then shift all following units down one
                    if ( grid[ gridIndex + i ] == data.unitID )
                    {
                        int shiftIndex = gridIndex + i;
                        int endOfCell = gridIndex + CELL_CAPACITY - 1;
                        while ( grid[ shiftIndex ] != VOID_CELL_VALUE && shiftIndex < endOfCell ) // remove the unit by shifting all following units down one
                        {
                            grid[ shiftIndex ] = grid[ shiftIndex + 1 ];
                            shiftIndex++;
                        }

                        grid[ shiftIndex ] = VOID_CELL_VALUE;
                        break;
                    }
                }

                // add the unit id to the new cell it occupies
                gridIndex = data.newCell * CELL_CAPACITY;

                for ( int i = 0; i < CELL_CAPACITY; i++ )
                {
                    if ( grid[ gridIndex + i ] == VOID_CELL_VALUE )
                    {
                        grid[ gridIndex + i ] = ( ushort ) data.unitID;
                        break;
                    }
                }
            }

            cellData.Clear();
        }
    }
    [BurstCompile]
    private struct CopyPositionsJob : IJobEntityBatchWithIndex
    {
        [NativeDisableParallelForRestriction] public NativeArray<float3> copyArray;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int firstEntityInQueryIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                copyArray[ firstEntityInQueryIndex + i ] = batchTranslation[ i ].Value;
            }
        }
    }
    [BurstCompile]
    private struct CopyVelocitesJob : IJobEntityBatchWithIndex
    {
        [NativeDisableParallelForRestriction] public NativeArray<float3> copyArray;
        [ReadOnly] public ComponentTypeHandle<Velocity> velocityHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int firstEntityInQueryIndex )
        {
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                copyArray[ firstEntityInQueryIndex + i ] = batchVelocity[ i ].Value;
            }
        }
    }
    [BurstCompile]
    private struct CopyMassJob : IJobEntityBatchWithIndex
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> copyArray;
        [ReadOnly] public ComponentTypeHandle<Mass> massHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int firstEntityInQueryIndex )
        {
            NativeArray<Mass> batchMass = batchInChunk.GetNativeArray( massHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                copyArray[ firstEntityInQueryIndex + i ] = ( byte ) batchMass[ i ].Value;
            }
        }
    }
    [BurstCompile]
    private struct FindCollidingPairsJob : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS;

        [ReadOnly] public NativeArray<ushort> grid;
        [ReadOnly] public NativeArray<float3> copyPositions;
        [NativeDisableParallelForRestriction] public NativeQueue<CollidingPair>.ParallelWriter collidingPairs;
        [NativeDisableParallelForRestriction] public NativeBitArray collisionFlags;

        public void Execute( int index )
        {
            collisionFlags.Set( index , true );

            float px = copyPositions[ index ].x;
            float pz = copyPositions[ index ].z;

            float xmin = px - RADIUS;
            float zmin = pz - RADIUS;
            float xmax = px + RADIUS;
            float zmax = pz + RADIUS;
            int bl = ( int ) ( math.floor( xmin / CELL_SIZE ) + math.floor( zmin / CELL_SIZE ) * CELLS_ACROSS );
            int br = ( int ) ( math.floor( xmax / CELL_SIZE ) + math.floor( zmin / CELL_SIZE ) * CELLS_ACROSS );
            int tl = ( int ) ( math.floor( xmin / CELL_SIZE ) + math.floor( zmax / CELL_SIZE ) * CELLS_ACROSS );
            int tr = ( int ) ( math.floor( xmax / CELL_SIZE ) + math.floor( zmax / CELL_SIZE ) * CELLS_ACROSS );
            FixedList128<int> newCells = new FixedList128<int>();
            newCells.Add( bl );
            if ( br != bl )
                newCells.Add( br );
            if ( tl != bl )
                newCells.Add( tl );
            if ( br != tl )
                newCells.Add( tr );

            for ( int i = 0; i < newCells.Length; i++ )
            {
                int gridIndex = newCells[ i ] * CELL_CAPACITY;
                int count = 0;

                while ( grid[ gridIndex + count ] != VOID_CELL_VALUE && count < CELL_CAPACITY )
                {
                    int otherUnitIndex = grid[ gridIndex + count ];

                    if ( !collisionFlags.IsSet( otherUnitIndex ) )
                    {
                        float px2 = copyPositions[ otherUnitIndex ].x;
                        float pz2 = copyPositions[ otherUnitIndex ].z;
                        float distance = math.sqrt( ( px - px2 ) * ( px - px2 ) + ( pz - pz2 ) * ( pz - pz2 ) );

                        if ( distance < RADIUS )
                        {
                            collidingPairs.Enqueue( new CollidingPair
                            {
                                unit1 = ( ushort ) index ,
                                unit2 = ( ushort ) otherUnitIndex ,
                                distance = distance
                            } );
                        }
                    }

                    count++;
                }
            }
        }
    }
    [BurstCompile]
    private struct ResolveCollisionsJob : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS;
        [ReadOnly] public int CELL_CAPACITY;

        [ReadOnly] public NativeArray<CollidingPair> collisionPairs;
        [NativeDisableParallelForRestriction] public NativeArray<float3> copyPositions;
        [NativeDisableParallelForRestriction] public NativeArray<float3> copyVelocities;
        [NativeDisableParallelForRestriction] public NativeArray<byte> copyMass;

        public void Execute( int index )
        {
            int unit1 = collisionPairs[ index ].unit1;
            int unit2 = collisionPairs[ index ].unit2;
            float distance = collisionPairs[ index ].distance;

            float px = copyPositions[ unit1 ].x;
            float py = copyPositions[ unit1 ].y;
            float pz = copyPositions[ unit1 ].z;
            float vx = copyVelocities[ unit1 ].x;
            float vy = copyVelocities[ unit1 ].y;
            float vz = copyVelocities[ unit1 ].z;
            float m = copyMass[ unit1 ];

            float px2 = copyPositions[ unit2 ].x;
            float py2 = copyPositions[ unit2 ].y;
            float pz2 = copyPositions[ unit2 ].z;
            float vx2 = copyVelocities[ unit2 ].x;
            float vy2 = copyVelocities[ unit2 ].y;
            float vz2 = copyVelocities[ unit2 ].z;
            float m2 = copyMass[ unit2 ];

            float overlap = 0.5f * ( distance - RADIUS );

            float ax =  ( overlap * ( px - px2 ) ) / ( distance + 0.01f );
            float az =  ( overlap * ( pz - pz2 ) ) / ( distance + 0.01f );

            float ax1 = px - ax;
            float az1 = pz - az;
            float ax2 = px2 + ax;
            float az2 = pz2 + az;

            float2 pos1 = new float2( px , pz );
            float2 pos2 = new float2( px2 , pz2 );
            float2 vel1 = new float2( vx , vz );
            float2 vel2 = new float2( vx2 , vz2 );
            float2 normal = math.normalizesafe( pos1 - pos2 );
            float a1 = math.dot( vel1 , normal );
            float a2 = math.dot( vel2 , normal );
            float pe = ( 2 * ( a1 - a2 ) ) / ( m + m2 );
            float2 nVel1 = vel1 - pe * m2 * normal;
            float2 nVel2 = vel2 + pe * m2 * normal;

            copyPositions[ unit1 ] = new float3( ax1 , py , az1 );
            copyVelocities[ unit1 ] = new float3( vx , vy , vz );
            copyPositions[ unit2 ] = new float3( ax2 , py2 , az2 );
            copyVelocities[ unit2 ] = new float3( vx2 , vy2 , vz2 );
        }
    }
    [BurstCompile]
    private struct WriteDataJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public NativeArray<float3> copyPositions;
        [ReadOnly] public NativeArray<float3> copyVelocities;

        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Translation> translationHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Velocity> velocityHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                batchTranslation[ i ] = new Translation { Value = copyPositions[ indexOfFirstEntityInQuery + i ] };
                batchVelocity[ i ] = new Velocity { Value = copyVelocities[ indexOfFirstEntityInQuery + i ] };
            }
        }
    }

    private struct UpdateCellData
    {
        public int unitID;
        public int oldCell;
        public int newCell;
    }
    private struct CollidingPair
    {
        public ushort unit1;
        public ushort unit2;
        public float distance;
    }
}
