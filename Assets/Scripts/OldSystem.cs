using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

// OLD

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
    // add more grids which are bigger and bigger cells for potential larger units
    private NativeArray<ushort> grid; // ushort because grids take up alot of memory if they are large
    private NativeQueue<UpdateCellData> cellsToUpdate;
    private NativeBitArray cellFlags;

    private const float CELL_SIZE = 1f;
    private const int CELLS_ACROSS = 3000;
    private const int CELL_CAPACITY = 6;
    private const ushort VOID_CELL_VALUE = 60000;

    private EntityQuery query;

    public bool rebuildGrid = false;

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        /*grid = new NativeArray<ushort>( CELLS_ACROSS * CELLS_ACROSS * CELL_CAPACITY , Allocator.Persistent , NativeArrayOptions.UninitializedMemory );
        cellsToUpdate = new NativeQueue<UpdateCellData>( Allocator.Persistent );
        cellFlags = new NativeBitArray( CELLS_ACROSS * CELLS_ACROSS , Allocator.Persistent , NativeArrayOptions.ClearMemory );
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
            grid = grid,
        };
        handle = buildGridJob.Schedule( query , handle );
        handle.Complete();
        Dependency = handle;*/
    }
    protected override void OnUpdate()
    {
        //CollisionSystem1();
    }
    protected override void OnDestroy()
    {
        /*grid.Dispose();
        cellsToUpdate.Dispose();
        cellFlags.Dispose();*/
        base.OnDestroy();
    }

    // NativeArray of pointers to linked lists points to array below
    // NativeArray sizeof number of units

    private void CollisionSystem1()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        NativeArray<CopyUnitData> copyArray = new NativeArray<CopyUnitData>( query.CalculateEntityCount() , Allocator.TempJob );

        JobHandle handle = new JobHandle();

        var updateCellsJob = new UpdateUnitCellsJob // Update the cell the unit is currently in and queue changed cells
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            unitsToUpdate = cellsToUpdate.AsParallelWriter() ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCell>()
        };
        handle = updateCellsJob.ScheduleParallel( query , 1 , Dependency );

        var copyJob = new CopyJob
        {
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>() ,
            copyArray = copyArray
        };
        var updateGridJob = new UpdateGridCellsJobOG // remove units from old cells and add to new ones
        {
            CELL_CAPACITY = CELL_CAPACITY ,
            VOID_CELL_VALUE = VOID_CELL_VALUE ,
            grid = grid ,
            cellData = cellsToUpdate
        };
        handle = JobHandle.CombineDependencies(
            updateGridJob.Schedule( handle ) ,
            copyJob.ScheduleParallel( query , 1 , handle ) );

        var resolveCollisionsJob = new ResolveCollisionsJobOG()
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            RADIUS = 0.5f , // can make this a component
            grid = grid ,
            copyUnitData = copyArray ,
        };
        handle = resolveCollisionsJob.Schedule( copyArray.Length , 80 , handle );

        var writeJob = new WriteDataJob
        {
            copyArray = copyArray ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            velocityHandle = GetComponentTypeHandle<Velocity>() ,
            massHandle = GetComponentTypeHandle<Mass>()
        };
        handle = writeJob.ScheduleParallel( query , 1 , handle );

        Dependency = copyArray.Dispose( handle );
    }

    private void TestSystem()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        JobHandle handle = new JobHandle();

        var updateUnitCells = new UpdateUnitCellsJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            unitsToUpdate = cellsToUpdate.AsParallelWriter() ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCell>()
        };
        handle = updateUnitCells.ScheduleParallel( query , 1 , Dependency );

        var testJob = new TestCellsJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            CELL_CAPACITY = CELL_CAPACITY ,
            grid = grid ,
            cellHandle = GetComponentTypeHandle<CollisionCell>()
        };
        handle = testJob.Schedule( query , handle );

        Dependency = handle;
    }

    private struct TestCellsJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public int CELL_CAPACITY;
        public NativeArray<ushort> grid;
        public ComponentTypeHandle<CollisionCell> cellHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<CollisionCell> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                int cell = batchCell[ i ].Value;
                int gridIndex = cell * CELL_CAPACITY;

                for ( int j = 0; j < CELL_CAPACITY; j++ )
                {
                    UnityEngine.Debug.Log( grid[ gridIndex + j ] );
                }
            }
        }
    }

    // COMMON
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
                        grid[ gridIndex + j ] = ( ushort ) ( indexOfFirstEntityInQuery + i ); // store the unit id in the grid
                        break;
                    }
                }
            }
        }
    }
    [BurstCompile]
    private struct CopyJob : IJobEntityBatchWithIndex
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
    private struct WriteDataJob : IJobEntityBatchWithIndex
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

    // ORIGINAL
    [BurstCompile]
    private unsafe struct ResolveCollisionsJobOG : IJobParallelFor
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

            /*FixedList512<int> cellList = new FixedList512<int>();
            cellList.AddNoResize( curCell );
            cellList.AddNoResize( curCell - 1 + CELLS_ACROSS );
            cellList.AddNoResize( curCell + CELLS_ACROSS );
            cellList.AddNoResize( curCell + 1 + CELLS_ACROSS );
            cellList.AddNoResize( curCell - 1 );
            cellList.AddNoResize( curCell + 1 );
            cellList.AddNoResize( curCell - 1 - CELLS_ACROSS );
            cellList.AddNoResize( curCell - CELLS_ACROSS );
            cellList.AddNoResize( curCell + 1 - CELLS_ACROSS );*/

            FixedList512<int> ids = new FixedList512<int>();

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
                while ( count < CELL_CAPACITY && grid[ gridIndex + count ] != VOID_CELL_VALUE )
                {
                    if ( grid[ gridIndex + count ] != VOID_CELL_VALUE )
                        ids.AddNoResize( grid[ gridIndex + count ] );

                    count++;
                }
            }

            /*for ( int j = 0; j < cellList.Length; j++ )
            {
                int gridIndex = cellList[ j ] * CELL_CAPACITY;
                int count = 0;
                while ( count < CELL_CAPACITY )
                {
                    if ( grid[ gridIndex + count ] != VOID_CELL_VALUE )
                        ids.AddNoResize( grid[ gridIndex + count ] );

                    count++;
                }
            }*/

            for ( int j = 0; j < ids.Length; j++ )
            {
                int otherUnitIndex = ids[ j ];
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
    [BurstCompile]
    private struct UpdateGridCellsJob : IJob
    {
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public ushort VOID_CELL_VALUE;

        public NativeArray<ushort> grid;
        public NativeQueue<UpdateCellData> cellData;

        public void Execute()
        {
            while ( cellData.TryDequeue( out UpdateCellData data ) ) // REMOVE UNIT FROM CELL
            {
                int gridIndex = data.oldCell * CELL_CAPACITY;

                for ( int i = 0; i < CELL_CAPACITY; i++ )
                {
                    if ( grid[ gridIndex + i ] == data.unitID ) // REMOVE UNIT AND SHIFT REST DOWN BY ONE
                    {
                        while ( i < CELL_CAPACITY - 1 )
                        {
                            grid[ gridIndex + i ] = grid[ gridIndex + i + 1 ];
                            i++;
                        }
                        grid[ i ] = VOID_CELL_VALUE;
                        break;
                    }
                }
                
                gridIndex = data.newCell * CELL_CAPACITY; // ADD UNIT TO CELL

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
    [BurstCompile] // ALSO DO STATIC "COLLISIONS" OF A RADIUS OF DOUBLE AND JUST APPLY VERY MINOR REPULSE FORCE TO KEEP UNITS APART BEFORE THEY EVEN TOUCH
    private struct ResolveCollisionsJob : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS;
        [ReadOnly] public int CELL_CAPACITY;

        [ReadOnly] public NativeArray<ushort> grid;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        public void Execute( int index )
        {
            float px = copyUnitData[ index ].position.x;
            float pz = copyUnitData[ index ].position.z;
            float vx = copyUnitData[ index ].velocity.x;
            float vz = copyUnitData[ index ].velocity.z;
            float m = copyUnitData[ index ].mass;

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

                    copyUnitData[ index ] = new CopyUnitData
                    {
                        position = new float3( ax1 , copyUnitData[ index ].position.y , az1 ) ,
                        velocity = new float3( nVel1.x , copyUnitData[ index ].velocity.y , nVel1.y ) ,
                        mass = copyUnitData[ index ].mass
                    };

                    copyUnitData[ otherUnitIndex ] = new CopyUnitData
                    {
                        position = new float3( ax2 , copyUnitData[ index ].position.y , az2 ) ,
                        velocity = new float3( nVel2.x , copyUnitData[ index ].velocity.y , nVel2.y ) ,
                        mass = copyUnitData[ index ].mass
                    };

                    count++;
                }
            }
        }
    }
    [BurstCompile]
    private struct UpdateGridCellsJobOG : IJob
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

                    /*if ( grid[ gridIndex + i ] == data.unitID )
                    {
                        grid[ gridIndex + i ] = VOID_CELL_VALUE;
                        break;
                    }*/
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
    // NO UNIT SHIFT ON UPDATE GRID
    [BurstCompile]
    private struct UpdateGridCellsJob2 : IJob
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
                        grid[ gridIndex + i ] = VOID_CELL_VALUE;
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
    private struct ResolveCollisionsJobV2 : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS; // can be entity dependent obviously
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public int VOID_CELL_VALUE;

        [ReadOnly] public NativeArray<ushort> grid;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        public void Execute( int i )
        {
            // LOCAL VARIABLES UNIT 1
            float px = copyUnitData[ i ].position.x;
            float pz = copyUnitData[ i ].position.z;
            float vx = copyUnitData[ i ].velocity.x;
            float vz = copyUnitData[ i ].velocity.z;
            float m = copyUnitData[ i ].mass;

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

            FixedList512<int> ids = new FixedList512<int>();
            for ( int j = 0; j < newCells.Length; j++ )
            {
                int gridIndex = newCells[ j ] * CELL_CAPACITY;
                for ( int k = 0; k < CELL_CAPACITY; k++ )
                {
                    if ( grid[ gridIndex + k ] != VOID_CELL_VALUE )
                        ids.AddNoResize( grid[ gridIndex + k ] );
                }
            }

            for ( int j = 0; j < ids.Length; j++ )
            {
                int otherUnitIndex = grid[ ids[ j ] ];
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

                copyUnitData[ i ] = new CopyUnitData
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
                };
            }
        }
    }

    // BIT FLAGS
    [BurstCompile]
    private struct ResolveCollisionsJob2 : IJobParallelFor
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS;

        [ReadOnly] public NativeArray<ushort> grid;
        [NativeDisableParallelForRestriction] public NativeBitArray cellFlags;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        public void Execute( int index )
        {
            float testPX = copyUnitData[ index ].position.x;
            float testPZ = copyUnitData[ index ].position.z;
            int cell = ( int ) ( math.floor( testPX / CELL_SIZE ) + math.floor( testPZ / CELL_SIZE ) * CELLS_ACROSS );

            if ( true )
            {
                FixedList512<int> indices = new FixedList512<int>();
                int curCell = cell;

                // check current and east cells
                for ( int i = 0; i < 2; i++ )
                {
                    int gridIndex = curCell * CELL_CAPACITY;
                    int count = 0;
                    while ( grid[ gridIndex ] != VOID_CELL_VALUE && count < CELL_CAPACITY )
                    {
                        indices.AddNoResize( grid[ gridIndex ] );
                        count++;
                        gridIndex++;
                    }

                    curCell ++;
                }

                curCell = cell - 1 - CELLS_ACROSS;
                // check sw, s, se cells
                for ( int i = 0; i < 3; i++ )
                {
                    int gridIndex = curCell * CELL_CAPACITY;
                    int count = 0;
                    while ( grid[ gridIndex ] != VOID_CELL_VALUE && count < CELL_CAPACITY )
                    {
                        indices.AddNoResize( grid[ gridIndex ] );
                        count++;
                        gridIndex++;
                    }

                    curCell ++;
                }

                // do collisions
                int cmpStart = 1;
                for ( int cur = 0; cur < indices.Length - 1; cur++ )
                {
                    int curI = indices[ cur ]; 

                    for ( int cmp = cmpStart; cmp < indices.Length; cmp++ )
                    {
                        int cmpI = indices[ cmp ];

                        float px = copyUnitData[ curI ].position.x;
                        float pz = copyUnitData[ curI ].position.z;
                        float vx = copyUnitData[ curI ].velocity.x;
                        float vz = copyUnitData[ curI ].velocity.z;
                        float m = copyUnitData[ curI ].mass;
                        float px2 = copyUnitData[ cmpI ].position.x;
                        float pz2 = copyUnitData[ cmpI ].position.z;
                        float vx2 = copyUnitData[ cmpI ].velocity.x;
                        float vz2 = copyUnitData[ cmpI ].velocity.z;
                        float m2 = copyUnitData[ cmpI ].mass;

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

                        copyUnitData[ curI ] = new CopyUnitData
                        {
                            position = new float3( ax1 , copyUnitData[ curI ].position.y , az1 ) ,
                            velocity = new float3( nVel1.x , copyUnitData[ curI ].velocity.y , nVel1.y ) ,
                            mass = copyUnitData[ curI ].mass
                        };
                        copyUnitData[ cmpI ] = new CopyUnitData
                        {
                            position = new float3( ax2 , copyUnitData[ cmpI ].position.y , az2 ) ,
                            velocity = new float3( nVel2.x , copyUnitData[ cmpI ].velocity.y , nVel2.y ) ,
                            mass = copyUnitData[ cmpI ].mass
                        };
                    }

                    cmpStart++;
                }
            }
        }
    }
    [BurstCompile]
    private struct ResolveCollisions3 : IJobParallelFor
    {
        [ReadOnly] public int CELL_CAPACITY;
        [ReadOnly] public int CELLS_ACROSS;

        [ReadOnly] public NativeArray<ushort> grid;
        [NativeDisableParallelForRestriction] public NativeBitArray cellFlags;
        [NativeDisableParallelForRestriction] public NativeArray<CopyUnitData> copyUnitData;

        public void Execute( int index )
        {
            float px = copyUnitData[ index ].position.x;
            float pz = copyUnitData[ index ].position.z;
            int cell = ( int ) ( math.floor( px ) + math.floor( pz ) * CELLS_ACROSS );

            if ( !cellFlags.IsSet( cell ) )
            {
                cellFlags.Set( cell , true );


            }
        }
    }
    [BurstCompile]
    private struct ClearFlagsJob : IJob
    {
        public NativeBitArray flags;

        public void Execute()
        {
            flags.Clear();
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
