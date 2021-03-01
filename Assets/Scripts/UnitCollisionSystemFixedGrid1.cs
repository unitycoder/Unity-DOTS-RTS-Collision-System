using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

// Grid containes ID's of entities in cells

// Store current cell with entities
// Update this value every frame
// If the unit has changed cells, write the old and new cells to a native queue

// Update the grid based on the queue ( remove ID from old cell, shift the rest of the units down, add the ID into new cell)
// At same time copy unit physics data to structured array

// Resolve collisions using the copy data

// Write data back to entities

// Since grid stores state over frames, it must be rebuilt once in a while once you start removing entities
// I do this after every 1-2 thousand removals, not noticable


public class UnitCollisionSystemFixedGrid2 : SystemBase
{
    private NativeArray<ushort> spatialGrid;
    private NativeQueue<UpdateCellData> cellsToUpdate;
    private NativeHashSet<int> activeCellsMap;

    private const float CELL_SIZE = 1f;
    private const int CELLS_ACROSS = 4000;
    private const int CELL_CAPACITY = 4;
    private const ushort VOID_CELL_VALUE = 64999;

    private EntityQuery query;

    public bool rebuildGrid = false;
    private int frame = 0;

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        /*spatialGrid = new NativeArray<ushort>( CELLS_ACROSS * CELLS_ACROSS * CELL_CAPACITY , Allocator.Persistent , NativeArrayOptions.UninitializedMemory );
        cellsToUpdate = new NativeQueue<UpdateCellData>( Allocator.Persistent );
        activeCellsMap = new NativeHashSet<int>( 300000 , Allocator.Persistent );
        query = GetEntityQuery( typeof( UnitTag ) );

        var initJob = new InitializeGridJob // set all grid values to void value
        {
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            spatialGrid = spatialGrid ,
        };
        JobHandle handle = initJob.Schedule( Dependency );
        handle.Complete();
        var buildGridJob = new BuildGridJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            RADIUS = 0.5f ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCellMulti>() ,
            grid = spatialGrid ,
            activeCellsMap = activeCellsMap
        };
        handle = buildGridJob.Schedule( query , handle );
        handle.Complete();
        Dependency = handle;*/
    }
    protected override void OnUpdate()
    {
        //CollisionSystem2();
    }
    protected override void OnDestroy()
    {
        /*spatialGrid.Dispose();
        cellsToUpdate.Dispose();
        activeCellsMap.Dispose();*/
        base.OnDestroy();
    }

    private void CollisionSystem1()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        NativeArray<CopyUnitData> copyUnitData = new NativeArray<CopyUnitData>( query.CalculateEntityCount() , Allocator.TempJob );

        var handle = new JobHandle();

        var copyJob = new CopyUnitDataJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>() ,
            copyArray = copyUnitData
        };

        if ( frame == 0 )
        {
            var calculateNewCellsJob = new CalculateNewCellsJob
            {
                CELL_SIZE = CELL_SIZE ,
                CELLS_ACROSS = CELLS_ACROSS ,
                RADIUS = 0.5f ,
                cellsToUpdate = cellsToUpdate.AsParallelWriter() ,
                cellHandle = GetComponentTypeHandle<CollisionCellMulti>() ,
                translationHandle = GetComponentTypeHandle<Translation>()
            };
            handle = calculateNewCellsJob.ScheduleParallel( query , 1 , Dependency );

            var updateCellsJob = new UpdateCellsJob
            {
                CELL_CAPACITY = CELL_CAPACITY ,
                VOID_CELL_VALUE = VOID_CELL_VALUE ,
                cellDataQ = cellsToUpdate ,
                grid = spatialGrid
            };
            handle = JobHandle.CombineDependencies(
                copyJob.ScheduleParallel( query , 1 , handle ) ,
                updateCellsJob.Schedule( handle ) );
        }
        else
        {
            var recordActiveCellsJob = new RecordActiveCellsJob
            {
                cellDataQ = cellsToUpdate ,
                grid = spatialGrid ,
                activeCells = activeCellsMap
            };
            handle = JobHandle.CombineDependencies(
                copyJob.ScheduleParallel( query , 1 , Dependency ) ,
                recordActiveCellsJob.Schedule( Dependency ) );

            var clearCellsToUpdateJob = new ClearCellsToUpdateJob
            {
                queue = cellsToUpdate
            };

            handle = clearCellsToUpdateJob.Schedule( handle );
        }

        handle.Complete();

        NativeArray<int> activeCellsArray = activeCellsMap.ToNativeArray( Allocator.TempJob );
        var resolveCollisionJob = new ResolveCollisionsJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            RADIUS = 0.5f ,
            CELL_CAPACITY = CELL_CAPACITY ,
            grid = spatialGrid ,
            activeCells = activeCellsArray ,
            copyUnitData = copyUnitData
        };
        handle = resolveCollisionJob.Schedule( activeCellsArray.Length , 80 , handle );

        var writeToUnitsJob = new WriteUnitDataJob
        {
            copyArray = copyUnitData ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>()
        };
        handle = writeToUnitsJob.ScheduleParallel( query , 1 , handle );

        handle = copyUnitData.Dispose( handle );
        Dependency = activeCellsArray.Dispose( handle );

        frame++;
        if ( frame > 1 )
            frame = 0;
    }
    private unsafe void CollisionSystem2()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        NativeArray<CopyUnitData> copyUnitData = new NativeArray<CopyUnitData>( query.CalculateEntityCount() , Allocator.TempJob );

        /*for ( int i = 0; i < activeCellsMap.Count; i++ )
        {
            UnityEngine.Debug.Log( activeCellsMap[ i ] );
        }*/

        var handle = new JobHandle();

        var copyJob = new CopyUnitDataJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>() ,
            copyArray = copyUnitData
        };

        if ( frame == 0 )
        {
            var calculateNewCellsJob = new CalculateNewCellsJob
            {
                CELL_SIZE = CELL_SIZE ,
                CELLS_ACROSS = CELLS_ACROSS ,
                RADIUS = 0.5f ,
                cellsToUpdate = cellsToUpdate.AsParallelWriter() ,
                cellHandle = GetComponentTypeHandle<CollisionCellMulti>() ,
                translationHandle = GetComponentTypeHandle<Translation>()
            };
            handle = calculateNewCellsJob.ScheduleParallel( query , 1 , Dependency );

            var updateCellsJob = new UpdateCellsJob
            {
                CELL_CAPACITY = CELL_CAPACITY ,
                VOID_CELL_VALUE = VOID_CELL_VALUE ,
                cellDataQ = cellsToUpdate ,
                grid = spatialGrid
            };
            handle = JobHandle.CombineDependencies(
                copyJob.ScheduleParallel( query , 1 , handle ) ,
                updateCellsJob.Schedule( handle ) );
        }
        else
        {
            var recordActiveCellsJob = new RecordActiveCellsJob
            {
                cellDataQ = cellsToUpdate ,
                grid = spatialGrid ,
                activeCells = activeCellsMap
            };
            handle = JobHandle.CombineDependencies(
                copyJob.ScheduleParallel( query , 1 , Dependency ) ,
                recordActiveCellsJob.Schedule( Dependency ) );

            var clearCellsToUpdateJob = new ClearCellsToUpdateJob
            {
                queue = cellsToUpdate
            };

            handle = clearCellsToUpdateJob.Schedule( handle );
        }

        /*var resolveCollisionsJob = new ResolveCollisionsJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            RADIUS = 0.5f ,
            CELL_CAPACITY = CELL_CAPACITY ,
            grid = spatialGrid ,
            activeCells = &p ,
            copyUnitData = copyUnitData
        };*/



        /*var writeToUnitsJob = new WriteUnitDataJob
        {
            copyArray = copyUnitData ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>()
        };
        handle = writeToUnitsJob.ScheduleParallel( query , 1 , handle );*/

        handle = copyUnitData.Dispose( handle );
        Dependency = handle;

        frame++;
        if ( frame > 1 )
            frame = 0;
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
        [ReadOnly] public float RADIUS;

        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        public ComponentTypeHandle<CollisionCellMulti> cellHandle;
        public NativeArray<ushort> grid;
        public NativeHashSet<int> activeCellsMap;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCellMulti> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float pz = batchTranslation[ i ].Value.z;

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
                newCells.Add( math.select( br , -1 , br == bl ) );
                newCells.Add( math.select( tl , -1 , tl == bl ) );
                newCells.Add( math.select( tr , -1 , br == tl ) );
                /*newCells[ 0 ] = bl;
                newCells[ 1 ] = math.select( br , -1 , br == bl );
                newCells[ 2 ] = math.select( tl , -1 , tl == bl );
                newCells[ 3 ] = math.select( tr , -1 , br == tl );*/

                batchCell[ i ] = new CollisionCellMulti
                {
                    bL = newCells[ 0 ] ,
                    bR = newCells[ 1 ] ,
                    tL = newCells[ 2 ] ,
                    tR = newCells[ 3 ]
                };

                for ( int j = 0; j < 4; j++ )
                {
                    if ( newCells[ j ] != -1 )
                    {
                        if ( !activeCellsMap.Contains( newCells[ j ] ) )
                            activeCellsMap.Add( newCells[ j ] );

                        int gridIndex = newCells[ j ] * CELL_CAPACITY;
                        int count = 0;
                        while ( count < CELL_CAPACITY )
                        {
                            if ( grid[ gridIndex + count ] != VOID_CELL_VALUE )
                            {
                                grid[ gridIndex + count ] = ( ushort ) ( indexOfFirstEntityInQuery + i ); // store the unit id in the grid
                                break;
                            }

                            count++;
                        }
                    }
                }
            }
        }
    }
    [BurstCompile]
    private struct CalculateNewCellsJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS;

        public NativeQueue<UpdateCellData>.ParallelWriter cellsToUpdate;
        public ComponentTypeHandle<CollisionCellMulti> cellHandle;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCellMulti> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float pz = batchTranslation[ i ].Value.z;

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
                newCells.Add( math.select( br , -1 , br == bl ) );
                newCells.Add( math.select( tl , -1 , tl == bl ) );
                newCells.Add( math.select( tr , -1 , br == tl ) );
                /*newCells[ 0 ] = bl;
                newCells[ 1 ] = math.select( br , -1 , br == bl );
                newCells[ 2 ] = math.select( tl , -1 , tl == bl );
                newCells[ 3 ] = math.select( tr , -1 , br == tl );*/

                CollisionCellMulti oldCell = batchCell[ i ];

                FixedList128<int> oldCells = new FixedList128<int>();
                oldCells.Add( oldCell.bL );
                oldCells.Add( oldCell.bR );
                oldCells.Add( oldCell.tL );
                oldCells.Add( oldCell.tR );
                /*oldCells[ 0 ] = oldCell.bL;
                oldCells[ 1 ] = oldCell.bR;
                oldCells[ 2 ] = oldCell.tL;
                oldCells[ 3 ] = oldCell.tR;*/

                for ( int j = 0; j < 4; j++ )
                {
                    if ( newCells[ j ] != oldCells[ j ] )
                    {
                        UpdateCellData data = new UpdateCellData
                        {
                            unitID = indexOfFirstEntityInQuery + i ,
                            oldCell = oldCells[ j ] ,
                            newCell = newCells[ j ]
                        };

                        cellsToUpdate.Enqueue( data );
                    }
                }

                batchCell[ i ] = new CollisionCellMulti
                {
                    bL = bl ,
                    bR = newCells[ 1 ] ,
                    tL = newCells[ 2 ] ,
                    tR = newCells[ 3 ]
                };
            }
        }
    }
    [BurstCompile]
    private struct CopyUnitDataJob : IJobEntityBatchWithIndex
    {
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyArray;

        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<Velocity> velocityHandle;
        [ReadOnly] public ComponentTypeHandle<Mass> massHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int firstEntityInQueryIndex )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );
            NativeArray<Mass> batchMass = batchInChunk.GetNativeArray( massHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                copyArray[ firstEntityInQueryIndex + i ] = new CopyUnitData
                {
                    position = batchTranslation[ i ].Value ,
                    velocity = batchVelocity[ i ].Value ,
                    mass = batchMass[ i ].Value
                };
            }
        }
    }
    [BurstCompile]
    private struct UpdateCellsJob : IJob
    {
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public ushort VOID_CELL_VALUE;
        public NativeQueue<UpdateCellData> cellDataQ;
        public NativeArray<ushort> grid;

        public void Execute()
        {
            NativeArray<UpdateCellData> cellData = cellDataQ.ToArray( Allocator.Temp );

            for ( int i = 0; i < cellData.Length; i++ )
            {
                UpdateCellData data = cellData[ i ];
                int gridIndex = data.oldCell * CELL_CAPACITY;

                if ( gridIndex >= 0 )
                {
                    for ( int j = 0; j < CELL_CAPACITY; j++ )
                    {
                        // basically loop through the cell until id is found
                        // then shift all following units down one
                        if ( grid[ gridIndex + j ] == data.unitID )
                        {
                            int shiftIndex = gridIndex + j;
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
                }

                // add the unit id to the new cell it occupies
                gridIndex = data.newCell * CELL_CAPACITY;

                if ( gridIndex >= 0 )
                {
                    for ( int j = 0; j < CELL_CAPACITY; j++ )
                    {
                        if ( grid[ gridIndex + j ] == VOID_CELL_VALUE )
                        {
                            grid[ gridIndex + j ] = ( ushort ) data.unitID;
                            break;
                        }
                    }
                }
            }

            cellData.Dispose();
        }
    }
    [BurstCompile]
    private struct RecordActiveCellsJob : IJob
    {
        [ReadOnly] public NativeQueue<UpdateCellData> cellDataQ;
        [ReadOnly] public NativeArray<ushort> grid;
        public NativeHashSet<int> activeCells;

        public void Execute()
        {
            NativeArray<UpdateCellData> cellData = cellDataQ.ToArray( Allocator.Temp );

            for ( int i = 0; i < cellData.Length; i++ )
            {
                UpdateCellData data = cellData[ i ];
                int gridIndex = data.oldCell * CELL_CAPACITY;

                if ( gridIndex >= 0 )
                {
                    if ( grid[ gridIndex ] == VOID_CELL_VALUE && activeCells.Contains( data.oldCell ) )
                        activeCells.Remove( data.oldCell );
                }

                gridIndex = data.newCell * CELL_CAPACITY;

                if ( gridIndex >= 0 && !activeCells.Contains( data.newCell ) )
                    activeCells.Add( data.newCell );
            }

            cellData.Dispose();
        }
    }
    [BurstCompile]
    private unsafe struct GetActiveCellsJob : IJob
    {
        public NativeHashSet<int> map;
        public NativeArray<int> cells;

        public void Execute()
        {
            cells = map.ToNativeArray( Allocator.TempJob );
        }
    }
    [BurstCompile]
    private unsafe struct GetActiveCellsJob2 : IJob
    {
        [NativeDisableUnsafePtrRestriction] public void* ap;
        [NativeDisableUnsafePtrRestriction] public int* ip;
        public NativeHashSet<int> map;

        public void Execute()
        {
            NativeArray<int> cells = map.ToNativeArray( Allocator.Temp );
            ip = ( int* ) cells.GetUnsafePtr();
            ap = cells.GetUnsafePtr();
        }
    }
    [BurstCompile]
    private unsafe struct ResolveCollisionsJob : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS; // can be entity dependent obviously
        [ReadOnly] public int CELL_CAPACITY;

        [ReadOnly] public NativeArray<ushort> grid;
        [ReadOnly] public NativeArray<int> activeCells;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        public void Execute( int index )
        {
            int gridIndex = activeCells[ index ] * CELL_CAPACITY;
            int compareStart = 1;

            FixedList128<int> ids = new FixedList128<int>();
            FixedList512<CopyUnitData> copyDataTemp = new FixedList512<CopyUnitData>();

            int count = 0;
            while ( count < CELL_CAPACITY && grid[ gridIndex + count ] != VOID_CELL_VALUE )
            {
                ids.AddNoResize( grid[ gridIndex + count ] );
                copyDataTemp.AddNoResize( copyUnitData[ grid[ gridIndex + count ] ] );
                count++;
            }

            for ( int i = 0; i < copyDataTemp.Length - 1; i++ )
            {
                for ( int j = compareStart; j < copyDataTemp.Length; j++ )
                {
                    float px = copyDataTemp[ i ].position.x;
                    float pz = copyDataTemp[ i ].position.z;
                    float vx = copyDataTemp[ i ].velocity.x;
                    float vz = copyDataTemp[ i ].velocity.z;
                    float m = copyDataTemp[ i ].mass;
                    float px2 = copyDataTemp[ j ].position.x;
                    float pz2 = copyDataTemp[ j ].position.z;
                    float vx2 = copyDataTemp[ j ].velocity.x;
                    float vz2 = copyDataTemp[ j ].velocity.z;
                    float m2 = copyDataTemp[ j ].mass;

                    float distance = math.sqrt( ( px - px2 ) * ( px - px2 ) + ( pz - pz2 ) * ( pz - pz2 ) );
                    int overlaps = math.select( 0 , 1 , distance < RADIUS );

                    float overlap = 0.5f * ( distance - RADIUS );

                    float ax = overlaps * ( overlap * ( px - px2 ) ) / ( distance + 0.01f );
                    float az = overlaps * ( overlap * ( pz - pz2 ) ) / ( distance + 0.01f );

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

                    copyDataTemp[ i ] = new CopyUnitData
                    {
                        position = new float3( ax1 , copyDataTemp[ i ].position.y , az1 ) ,
                        velocity = new float3( nVel1.x , copyDataTemp[ i ].velocity.y , nVel1.y ) ,
                        mass = copyDataTemp[ i ].mass
                    };
                    copyDataTemp[ j ] = new CopyUnitData
                    {
                        position = new float3( ax2 , copyDataTemp[ j ].position.y , az2 ) ,
                        velocity = new float3( nVel2.x , copyDataTemp[ j ].velocity.y , nVel2.y ) ,
                        mass = copyDataTemp[ j ].mass
                    };
                }

                compareStart++;
            }

            for ( int i = 0; i < copyDataTemp.Length; i++ )
                copyUnitData[ ids[ i ] ] = copyDataTemp[ i ];
        }
    }
    [BurstCompile]
    private struct ResolveCollisionsJob2 : IJob
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS; // can be entity dependent obviously
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public int NUM_SLICES;
        [ReadOnly] public int CURRENT_SLICE;

        [ReadOnly] public NativeArray<ushort> grid;
        [ReadOnly] public NativeHashSet<int> activeCells;
        [NativeDisableParallelForRestriction][NativeDisableContainerSafetyRestriction] public NativeArray<CopyUnitData> copyUnitData;

        public void Execute()
        {
            NativeArray<int> cells = activeCells.ToNativeArray( Allocator.Temp );
            int sliceSize = cells.Length / NUM_SLICES;
            int startIndex = CURRENT_SLICE * sliceSize;
            int endIndex = startIndex + sliceSize;

            for ( int index = startIndex; index < endIndex; index++ )
            {
                int gridIndex = cells[ index ] * CELL_CAPACITY;
                int compareStart = 1;

                FixedList128<int> ids = new FixedList128<int>();
                FixedList512<CopyUnitData> copyDataTemp = new FixedList512<CopyUnitData>();

                int count = 0;
                while ( count < CELL_CAPACITY && grid[ gridIndex + count ] != VOID_CELL_VALUE )
                {
                    ids.AddNoResize( grid[ gridIndex + count ] );
                    copyDataTemp.AddNoResize( copyUnitData[ grid[ gridIndex + count ] ] );
                    count++;
                }

                for ( int i = 0; i < count - 1; i++ )
                {
                    for ( int j = compareStart; j < count; j++ )
                    {
                        float px = copyDataTemp[ i ].position.x;
                        float pz = copyDataTemp[ i ].position.z;
                        float vx = copyDataTemp[ i ].velocity.x;
                        float vz = copyDataTemp[ i ].velocity.z;
                        float m = copyDataTemp[ i ].mass;
                        float px2 = copyDataTemp[ j ].position.x;
                        float pz2 = copyDataTemp[ j ].position.z;
                        float vx2 = copyDataTemp[ j ].velocity.x;
                        float vz2 = copyDataTemp[ j ].velocity.z;
                        float m2 = copyDataTemp[ j ].mass;

                        float distance = math.sqrt( ( px - px2 ) * ( px - px2 ) + ( pz - pz2 ) * ( pz - pz2 ) );
                        int overlaps = math.select( 0 , 1 , distance < RADIUS );

                        float overlap = 0.5f * ( distance - RADIUS );

                        float ax = overlaps * ( overlap * ( px - px2 ) ) / ( distance + 0.01f );
                        float az = overlaps * ( overlap * ( pz - pz2 ) ) / ( distance + 0.01f );

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

                        copyDataTemp[ i ] = new CopyUnitData
                        {
                            position = new float3( ax1 , copyDataTemp[ i ].position.y , az1 ) ,
                            velocity = new float3( nVel1.x , copyDataTemp[ i ].velocity.y , nVel1.y ) ,
                            mass = copyDataTemp[ i ].mass
                        };
                        copyDataTemp[ j ] = new CopyUnitData
                        {
                            position = new float3( ax2 , copyDataTemp[ j ].position.y , az2 ) ,
                            velocity = new float3( nVel2.x , copyDataTemp[ j ].velocity.y , nVel2.y ) ,
                            mass = copyDataTemp[ j ].mass
                        };
                    }

                    compareStart++;
                }

                for ( int i = 0; i < count; i++ )
                    copyUnitData[ ids[ i ] ] = copyDataTemp[ i ];
            }

            cells.Dispose();
        }
    }
    [BurstCompile]
    private struct WriteUnitDataJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public NativeArray<CopyUnitData> copyArray;

        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Translation> translationHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Velocity> velocityHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Mass> massHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<Velocity> batchVelocity = batchInChunk.GetNativeArray( velocityHandle );
            NativeArray<Mass> batchMass = batchInChunk.GetNativeArray( massHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                batchTranslation[ i ] = new Translation { Value = copyArray[ indexOfFirstEntityInQuery + i ].position };
                batchVelocity[ i ] = new Velocity { Value = copyArray[ indexOfFirstEntityInQuery + i ].velocity };
                batchMass[ i ] = new Mass { Value = copyArray[ indexOfFirstEntityInQuery + i ].mass };
            }
        }
    }
    [BurstCompile]
    private unsafe struct DisposeActiveCellsJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public void* p;

        public void Execute()
        {
            UnsafeUtility.Free( p , Allocator.Temp );
        }
    }
    [BurstCompile]
    private struct ClearCellsToUpdateJob : IJob
    {
        public NativeQueue<UpdateCellData> queue;

        public void Execute()
        {
            queue.Clear();
        }
    }

    private struct UpdateCellData
    {
        public int unitID;
        public int oldCell;
        public int newCell;
    }
    private struct CopyUnitData
    {
        public float3 position;
        public float3 velocity;
        public float mass;
    }
}
