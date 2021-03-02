using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

// OLD

public class UnitCollisionSystemMap : SystemBase
{
    private NativeMultiHashMap<int , int> map;
    private NativeQueue<UpdateCellData> cellsToUpdate;

    private const float CELL_SIZE = 2f;
    private const int CELLS_ACROSS = 3000;
    private const int CELL_CAPACITY = 10;
    private const ushort VOID_CELL_VALUE = 60000;
    private const int NUM_UNITS = 40000;

    private EntityQuery query;

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        /*map = new NativeMultiHashMap<int , int>( NUM_UNITS * 5 , Allocator.Persistent );
        cellsToUpdate = new NativeQueue<UpdateCellData>( Allocator.Persistent );

        query = GetEntityQuery( typeof( UnitTag ) );*/

        

        /*InitJob initJob = new InitJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            translationHandle = GetComponentTypeHandle<Translation>() ,
            cellHandle = GetComponentTypeHandle<CollisionCellMulti>()
        };

        Dependency = initJob.ScheduleParallel( query , 1 , Dependency );*/
    }
    protected override void OnUpdate()
    {
        //System3();
    }
    protected override void OnDestroy()
    {
        /*map.Dispose();
        cellsToUpdate.Dispose();*/
        base.OnDestroy();
    }

    private void System1()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        JobHandle handle = new JobHandle();

        UpdateCellsJob updateCellsJob = new UpdateCellsJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            unitsToUpdate = cellsToUpdate.AsParallelWriter() ,
            cellHandle = GetComponentTypeHandle<CollisionCellMulti>() ,
            translationHandle = GetComponentTypeHandle<Translation>()
        };

        handle = updateCellsJob.ScheduleParallel( query , 1 , Dependency );

        ClearQueueJob clearQueueJob = new ClearQueueJob
        {
            q = cellsToUpdate ,
        };

        handle = clearQueueJob.Schedule( handle );

        Dependency = handle;
    }
    private void System2()
    {
        query = GetEntityQuery( typeof( UnitTag ) );
        JobHandle handle = new JobHandle();

        FillMapJob fillMapJob = new FillMapJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            map = map.AsParallelWriter() ,
            translationHandle = GetComponentTypeHandle<Translation>()
        };

        handle = fillMapJob.ScheduleParallel( query , 1 , Dependency );

        ClearMapJob clearMapJob = new ClearMapJob
        {
            map = map 
        };

        handle = clearMapJob.Schedule( handle );

        Dependency = handle;
    }
    private void System3()
    {
        query = GetEntityQuery( typeof( UnitTag ) );

        JobHandle handle = new JobHandle();

        var fillJob = new FillMapJob
        {
            CELL_SIZE = CELL_SIZE ,
            CELLS_ACROSS = CELLS_ACROSS ,
            RADIUS = 0.25f ,
            map = map.AsParallelWriter() ,
            translationHandle = GetComponentTypeHandle<Translation>()
        };
        handle = fillJob.ScheduleParallel( query , 1 , Dependency );

        var clearJob = new ClearMapJob
        {
            map = map
        };
        handle = clearJob.Schedule( handle );

        Dependency = handle;
    }

    [BurstCompile]
    private struct InitJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;

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

                // GET CELLS TO CHECK
                int curCell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( pz / CELL_SIZE ) * CELLS_ACROSS );
                int xDir = math.select( 1 , -1 , math.round( px ) < px );
                int zDir = math.select( 1 , -1 , math.round( pz ) < pz );

                batchCell[ i ] = new CollisionCellMulti
                {
                    bL = ( ushort ) curCell ,
                    bR = ( ushort ) ( curCell + xDir ) ,
                    tL = ( ushort ) ( curCell + zDir * CELLS_ACROSS ) ,
                    tR = ( ushort ) ( curCell + xDir + zDir * CELLS_ACROSS )
                };
            }
        }
    }
    [BurstCompile]
    private struct UpdateCellsJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;

        public NativeQueue<UpdateCellData>.ParallelWriter unitsToUpdate;
        public ComponentTypeHandle<CollisionCellMulti> cellHandle;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;

        public unsafe void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );
            NativeArray<CollisionCellMulti> batchCell = batchInChunk.GetNativeArray( cellHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float px = batchTranslation[ i ].Value.x;
                float pz = batchTranslation[ i ].Value.z;

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

                CollisionCellMulti oldCells = batchCell[ i ];

                // LOOP OVER CELLS
                int* p = ( int* ) &cells;
                ushort* p2 = ( ushort* ) &oldCells;
                int length = UnsafeUtility.SizeOf<Cells>() / UnsafeUtility.SizeOf<int>();

                for ( int j = 0; j < length; j++ )
                {
                    if ( p[ j ] != p2[ j ] )
                    {
                        UpdateCellData data = new UpdateCellData
                        {
                            unitID = indexOfFirstEntityInQuery + i ,
                            oldCell = p2[ j ] ,
                            newCell = p[ j ]
                        };

                        unitsToUpdate.Enqueue( data );
                    }
                }
            }
        }
    }
    //[BurstCompile]
    [BurstCompile]
    private struct FillMapJob : IJobEntityBatchWithIndex
    {
        [ReadOnly] public float CELL_SIZE;
        [ReadOnly] public int CELLS_ACROSS;
        [ReadOnly] public float RADIUS;

        public NativeMultiHashMap<int , int>.ParallelWriter map;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;

        public unsafe void Execute( ArchetypeChunk batchInChunk , int batchIndex , int indexOfFirstEntityInQuery )
        {
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray( translationHandle );

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

                // GET CELLS TO CHECK
                int curCell = ( int ) ( math.floor( px / CELL_SIZE ) + math.floor( pz / CELL_SIZE ) * CELLS_ACROSS );
                int xDir = math.select( 1 , -1 , math.round( px ) < px );
                int zDir = math.select( 1 , -1 , math.round( pz ) < pz );

                FixedList128<int> newCells = new FixedList128<int>();
                newCells.Add( bl );
                if ( br != bl )
                    newCells.Add( br );
                if ( tl != bl )
                    newCells.Add( tl );
                if ( br != tl )
                    newCells.Add( tr );

                for ( int j = 0; j < newCells.Length; j++ )
                {
                    map.Add( newCells[ j ] , indexOfFirstEntityInQuery + i );
                }
            }
        }
    }
    [BurstCompile]
    private struct ClearQueueJob : IJob
    {
        public NativeQueue<UpdateCellData> q;

        public void Execute()
        {
            q.Clear();
        }
    }
    [BurstCompile]
    private struct ClearMapJob : IJob
    {
        public NativeMultiHashMap<int , int> map;

        public void Execute()
        {
            map.Clear();
        }
    }

    private struct Cells // contains current cell and 3 neighbours
    {
        public int cell;
        public int xN;
        public int zN;
        public int cN;
    }
    private struct UpdateCellData
    {
        public int unitID;
        public int oldCell;
        public int newCell;
    }
}
