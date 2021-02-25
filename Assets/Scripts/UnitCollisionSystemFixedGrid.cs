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


public class UnitCollisionSystemFixedGrid : SystemBase
{
    private NativeArray<ushort> spatialGrid; // ushort because grids take up alot of memory if they are large
    private NativeQueue<UpdateCellData> cellsToUpdate;

    private const float CELL_SIZE = 1f;
    private const int CELLS_ACROSS = 6000;
    private const int CELL_CAPACITY = 5;
    private const ushort VOID_CELL_VALUE = 60000;

    private EntityQuery query;

    public bool rebuildGrid = false;

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        spatialGrid = new NativeArray<ushort>( CELLS_ACROSS * CELLS_ACROSS , Allocator.Persistent , NativeArrayOptions.UninitializedMemory );
        cellsToUpdate = new NativeQueue<UpdateCellData>( Allocator.Persistent );
        query = GetEntityQuery( typeof( UnitTag ) );

        InitializeGridJob initJob = new InitializeGridJob // set all grid values to void value
        {
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            spatialGrid = spatialGrid ,
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
            grid = spatialGrid,
        };
        handle = buildGridJob.Schedule( query , handle );
        handle.Complete();
        Dependency = handle;
    }
    protected override void OnUpdate()
    {
        CollisionSystem1();
    }
    protected override void OnDestroy()
    {
        spatialGrid.Dispose();
        cellsToUpdate.Dispose();
        base.OnDestroy();
    }

    private void CollisionSystem1()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        NativeArray<CopyUnitData> copyArray = new NativeArray<CopyUnitData>( query.CalculateEntityCount() , Allocator.TempJob );

        JobHandle broadPhase = new JobHandle();

        // rebuild grid flag is up to you when to call in your program
        if ( rebuildGrid )
            broadPhase = RebuildBroadPhase( broadPhase , copyArray );
        else
            broadPhase = NormalBroadPhase( broadPhase , copyArray );

        Dependency = copyArray.Dispose( NarrowPhase( broadPhase , copyArray ) );
    }
    private JobHandle RebuildBroadPhase( JobHandle broadPhase , NativeArray<CopyUnitData> copyArray )
    {
        InitializeGridJob initJob = new InitializeGridJob
        {
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            spatialGrid = spatialGrid ,
        };
        JobHandle initHandle = initJob.Schedule( Dependency );

        BuildGridJob buildGridJob = new BuildGridJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCell>() ,
            grid = spatialGrid ,
        };
        CopyJob copyJob = new CopyJob // we still do physics on frame when we rebuild so we must still copy the values
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>() ,
            copyArray = copyArray
        };
        broadPhase = JobHandle.CombineDependencies(
            buildGridJob.Schedule( query , initHandle ) ,
            copyJob.Schedule( query , initHandle ) );

        return broadPhase;
    }
    private JobHandle NormalBroadPhase( JobHandle broadPhase , NativeArray<CopyUnitData> copyArray )
    {
        UpdateCellsJob updateCellsJob = new UpdateCellsJob // Update the cell the unit is currently in and queue changed cells
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            unitsToUpdate = cellsToUpdate.AsParallelWriter() ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCell>()
        };

        JobHandle updateCellsBarrier = updateCellsJob.ScheduleParallel( query , 1 , Dependency );

        CopyJob copyJob = new CopyJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>() ,
            copyArray = copyArray
        };

        UpdateGridJob updateGridJob = new UpdateGridJob // remove units from old cells and add to new ones
        {
            CELL_CAPACITY = CELL_CAPACITY ,
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            grid = spatialGrid ,
            cellData = cellsToUpdate
        };

        broadPhase = JobHandle.CombineDependencies(
            updateGridJob.Schedule( updateCellsBarrier ) ,
            copyJob.ScheduleParallel( query , 1 , updateCellsBarrier ) );

        return broadPhase;
    }
    private JobHandle NarrowPhase( JobHandle broadPhase , NativeArray<CopyUnitData> copyArray )
    {
        ResolveCollisionsJob resolveCollisionsJob = new ResolveCollisionsJob()
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            RADIUS = 0.5f , // can make this a component
            grid = spatialGrid ,
            copyUnitData = copyArray
        };

        JobHandle narrowPhase = resolveCollisionsJob.Schedule( copyArray.Length , 80 , broadPhase );

        WriteDataJob writeJob = new WriteDataJob
        {
            copyArray = copyArray ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>()
        };

        JobHandle writeBarrier = writeJob.ScheduleParallel( query , 1 , narrowPhase );

        return writeBarrier;
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
    private struct BuildGridJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public ushort VOID_CELL_VALUE;

        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        public ComponentTypeHandle<CollisionCell> cellHandle;
        public NativeArray<ushort> grid;

        [BurstCompile] // if you dont know, indexOfFirstEntityInQuery + i will give you the unit id, breaks when entities are added or deleted, then you must rebuild 
        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCell> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float py = batchTranslation[ i ].Value.z;
                // Can remove the divides if we decide to go with cell size of 1
                int cell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( py / CELL_SIZE ) * CELLS_ACROSS );
                batchCell[ i ] = new CollisionCell { Value = cell };

                int gridIndex = cell * CELL_CAPACITY;
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
    private struct UpdateCellsJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;

        public NativeQueue<UpdateCellData>.ParallelWriter unitsToUpdate;
        public ComponentTypeHandle<CollisionCell> cellHandle;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCell> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float py = batchTranslation[ i ].Value.z;
                // Can remove the divides if we decide to go with cell size of 1
                int cell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( py / CELL_SIZE ) * CELLS_ACROSS );
                int oldCell = batchCell[ i ].Value;
                batchCell[ i ] = new CollisionCell { Value = cell };

                if ( oldCell != cell )
                {
                    UpdateCellData data = new UpdateCellData
                    {
                        unitID = indexOfFirstEntityInQuery + i ,
                        oldCell = oldCell ,
                        newCell = cell
                    };

                    unitsToUpdate.Enqueue( data );
                }
            }
        }
    }
    [BurstCompile]
    private struct UpdateGridJob : IJob
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
                        //grid[ gridIndex + i ] = VOID_CELL_VALUE;
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
    private struct CopyJob : IJobEntityBatchWithIndex
    {
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyArray;

        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [ReadOnly] public ComponentTypeHandle<Velocity> velocityHandle;
        [ReadOnly] public ComponentTypeHandle<Mass> massHandle;

        [BurstCompile]
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
    private unsafe struct ResolveCollisionsJob : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS; // can be entity dependent obviously
        [ReadOnly] public int CELL_CAPACITY;

        [ReadOnly] public NativeArray<ushort> grid;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        private struct Cells // contains current cell and 3 neighbours
        {
            public int cell;
            public int xN;
            public int zN;
            public int cN;
        }

        public void Execute( int i )
        {
            // LOCAL VARIABLES UNIT 1
            float px = copyUnitData[ i ].position.x;
            float pz = copyUnitData[ i ].position.z;
            float vx = copyUnitData[ i ].velocity.x;
            float vz = copyUnitData[ i ].velocity.z;
            float m = copyUnitData[ i ].mass;

            // GET CELLS TO CHECK
            int curCell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( pz / CELL_SIZE ) * CELLS_ACROSS );
            int xDir = math.select( 1 , -1 , math.round( px ) < px );
            int zDir = math.select( 1 , -1 , math.round( pz ) < pz );

            Cells cells = new Cells
            {
                cell = curCell ,
                xN = curCell + xDir ,
                zN = curCell + zDir * CELLS_ACROSS ,
                cN = curCell + xDir + zDir * CELLS_ACROSS
            };

            // LOOP OVER CELLS
            int* p = ( int* ) &cells;
            int length = UnsafeUtility.SizeOf<Cells>() / UnsafeUtility.SizeOf<int>();

            for ( int j = 0; j < length; j++ )
            {
                int gridIndex = p[ j ] * CELL_CAPACITY;
                int count = 0;
                while ( grid[ gridIndex ] != VOID_CELL_VALUE && count < CELL_CAPACITY )
                {
                    int otherUnitIndex = grid[ gridIndex ];
                    float px2 = copyUnitData[ otherUnitIndex ].position.x;
                    float pz2 = copyUnitData[ otherUnitIndex ].position.z;
                    float vx2 = copyUnitData[ otherUnitIndex ].velocity.x;
                    float vz2 = copyUnitData[ otherUnitIndex ].velocity.z;
                    float m2 = copyUnitData[ otherUnitIndex ].mass;

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

                    /*copyUnitData[ i ] = new CopyUnitData
                    {
                        position = new float3( ax1 , copyUnitData[ i ].position.y , az1 ) ,
                        velocity = new float3( nVel1.x , copyUnitData[ i ].velocity.y , nVel1.y ) ,
                        mass = copyUnitData[ i ].mass
                    };

                    copyUnitData[ otherUnitIndex ] = new CopyUnitData
                    {
                        position = new float3( ax2 , copyUnitData[ i ].position.y , az2 ) ,
                        velocity = new float3( nVel2.x , copyUnitData[ i ].velocity.y , nVel2.y ) ,
                        mass = copyUnitData[ i ].mass
                    };*/

                    copyUnitData[ i ] = new CopyUnitData
                    {
                        position = new float3( ax1 , copyUnitData[ i ].position.y , az1 ) ,
                        velocity = new float3( vx , copyUnitData[ i ].velocity.y , vz ) ,
                        mass = copyUnitData[ i ].mass
                    };

                    copyUnitData[ otherUnitIndex ] = new CopyUnitData
                    {
                        position = new float3( ax2 , copyUnitData[ i ].position.y , az2 ) ,
                        velocity = new float3( vx2 , copyUnitData[ i ].velocity.y , vz2 ) ,
                        mass = copyUnitData[ i ].mass
                    };


                    gridIndex++;
                    count++;
                }
            }
        }
    }
    [BurstCompile]
    private unsafe struct ResolveCollisionsJobSIMD : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS; // can be entity dependent obviously
        [ReadOnly] public int CELL_CAPACITY;

        [ReadOnly] public NativeArray<ushort> grid;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        private struct Cells // contains current cell and 3 neighbours
        {
            public int cell;
            public int xN;
            public int zN;
            public int cN;
        }

        public void Execute( int i )
        {
            // LOCAL VARIABLES UNIT 1
            float px = copyUnitData[ i ].position.x;
            float pz = copyUnitData[ i ].position.z;
            float vx = copyUnitData[ i ].velocity.x;
            float vz = copyUnitData[ i ].velocity.z;
            float m = copyUnitData[ i ].mass;

            // GET CELLS TO CHECK
            int curCell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( pz / CELL_SIZE ) * CELLS_ACROSS );
            int xDir = math.select( 1 , -1 , math.round( px ) < px );
            int zDir = math.select( 1 , -1 , math.round( pz ) < pz );
            Cells cells = new Cells
            {
                cell = curCell ,
                xN = curCell + xDir ,
                zN = curCell + zDir * CELLS_ACROSS ,
                cN = curCell + xDir + zDir * CELLS_ACROSS
            };

            // LOOP OVER CELLS
            int* p = ( int* ) &cells;
            int length = UnsafeUtility.SizeOf<Cells>() / UnsafeUtility.SizeOf<int>();

            FixedList512<ushort> entities = new FixedList512<ushort>();

            for ( int j = 0; j < length; j++ )
            {
                int gridIndex = p[ j ] * CELL_CAPACITY;
                int count = 0;
                while ( grid[ gridIndex ] != VOID_CELL_VALUE && count < CELL_CAPACITY )
                {
                    entities.Add( grid[ gridIndex ] );
                    gridIndex++;
                    count++;
                }
            }

            int k = 0;
            for ( k=0; k + 4 < entities.Length; k+=4 )
            {
                int4 otherIndex = new int4( entities[ k ] , entities[ k + 1 ] , entities[ k + 2 ] , entities[ k + 3 ] );

                float4 px2 = new float4(
                    copyUnitData[ otherIndex.x ].position.x ,
                    copyUnitData[ otherIndex.y ].position.x ,
                    copyUnitData[ otherIndex.z ].position.x ,
                    copyUnitData[ otherIndex.w ].position.x );
                float4 pz2 = new float4(
                    copyUnitData[ otherIndex.x ].position.z ,
                    copyUnitData[ otherIndex.y ].position.z ,
                    copyUnitData[ otherIndex.z ].position.z ,
                    copyUnitData[ otherIndex.w ].position.z );

                float4 distance = math.sqrt( ( px - px2 ) * ( px - px2 ) + ( pz - pz2 ) * ( pz - pz2 ) );
                int4 overlaps = math.select( ( int4 ) 0 , 1 , distance < RADIUS );

                float4 overlap = 0.5f * ( distance - RADIUS );

                float4 ax = overlaps * ( overlap * ( px - px2 ) ) / ( distance + 0.01f );
                float4 az = overlaps * ( overlap * ( pz - pz2 ) ) / ( distance + 0.01f );

                float4 ax1 = px - ax;
                float4 az1 = pz - az;
                float4 ax2 = px2 + ax;
                float4 az2 = pz2 + az;

                float adjx = ( ax1.x + ax1.y + ax1.z + ax1.w ) / 4;
                float adjz = ( az1.x + az1.y + az1.z + az1.w ) / 4;

                copyUnitData[ i ] = new CopyUnitData
                {
                    position = new float3( adjx , 1 , adjz ) ,
                    velocity = new float3( vx , copyUnitData[ i ].velocity.y , vz ) ,
                    mass = copyUnitData[ i ].mass
                };

                copyUnitData[ otherIndex.x ] = new CopyUnitData
                {
                    position = new float3( ax2.x , 1 , az2.z ) ,
                    velocity = new float3( copyUnitData[ otherIndex.x ].velocity.x , copyUnitData[ i ].velocity.y , copyUnitData[ otherIndex.x ].velocity.z ) ,
                    mass = copyUnitData[ i ].mass
                };

                copyUnitData[ otherIndex.y ] = new CopyUnitData
                {
                    position = new float3( ax2.y , 1 , az2.y ) ,
                    velocity = new float3( copyUnitData[ otherIndex.y ].velocity.x , copyUnitData[ i ].velocity.y , copyUnitData[ otherIndex.y ].velocity.z ) ,
                    mass = copyUnitData[ i ].mass
                };

                copyUnitData[ otherIndex.z ] = new CopyUnitData
                {
                    position = new float3( ax2.z , 1 , az2.z ) ,
                    velocity = new float3( copyUnitData[ otherIndex.z ].velocity.x , copyUnitData[ i ].velocity.y , copyUnitData[ otherIndex.z ].velocity.z ) ,
                    mass = copyUnitData[ i ].mass
                };

                copyUnitData[ otherIndex.w ] = new CopyUnitData
                {
                    position = new float3( ax2.w , 1 , az2.w ) ,
                    velocity = new float3( copyUnitData[ otherIndex.w ].velocity.x , copyUnitData[ i ].velocity.y , copyUnitData[ otherIndex.w ].velocity.z ) ,
                    mass = copyUnitData[ i ].mass
                };
            }
            for ( k = k; k < entities.Length; k++)
            {
                int otherUnitIndex = entities[ k ];
                float px2 = copyUnitData[ otherUnitIndex ].position.x;
                float pz2 = copyUnitData[ otherUnitIndex ].position.z;
                float vx2 = copyUnitData[ otherUnitIndex ].velocity.x;
                float vz2 = copyUnitData[ otherUnitIndex ].velocity.z;
                float m2 = copyUnitData[ otherUnitIndex ].mass;

                float distance = math.sqrt( ( px - px2 ) * ( px - px2 ) + ( pz - pz2 ) * ( pz - pz2 ) );
                int overlaps = math.select( 0 , 1 , distance < RADIUS );

                float overlap = 0.5f * ( distance - RADIUS );

                float ax = overlaps * ( overlap * ( px - px2 ) ) / ( distance + 0.01f );
                float az = overlaps * ( overlap * ( pz - pz2 ) ) / ( distance + 0.01f );

                float ax1 = px - ax;
                float az1 = pz - az;
                float ax2 = px2 + ax;
                float az2 = pz2 + az;

                copyUnitData[ i ] = new CopyUnitData
                {
                    position = new float3( ax1 , copyUnitData[ i ].position.y , az1 ) ,
                    velocity = new float3( vx , copyUnitData[ i ].velocity.y , vz ) ,
                    mass = copyUnitData[ i ].mass
                };

                copyUnitData[ otherUnitIndex ] = new CopyUnitData
                {
                    position = new float3( ax2 , copyUnitData[ i ].position.y , az2 ) ,
                    velocity = new float3( vx2 , copyUnitData[ i ].velocity.y , vz2 ) ,
                    mass = copyUnitData[ i ].mass
                };
            }
        }
    }
    private struct WriteDataJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public NativeArray<CopyUnitData> copyArray;

        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Translation> translationHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Velocity> velocityHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Mass> massHandle;

        [BurstCompile]
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
