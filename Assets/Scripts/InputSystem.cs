using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;

[AlwaysUpdateSystem]
public class InputSystem : SystemBase
{
    #region Fields

    private CameraController cameraController;

    private float3 mouseStart = float3.zero;
    private int numClicked = 0;
    private double doubleClickTime = 0;
    private double doubleClickDelay = 0.5f;
    private double leftHoldTime = 0;
    private double leftHoldDelay = 0.25f;

    private JobHandle jobHandle;
    private EntityQuery entityQuery;

    #endregion

    protected override void OnStartRunning()
    {
        base.OnStartRunning();
        //cameraController = Camera.main.GetComponent<CameraController>();
    }
    protected override void OnUpdate()
    {
        jobHandle = new JobHandle();

        if ( Input.GetMouseButtonDown( 0 ) )
            On_LeftDown();
        else if ( Input.GetMouseButton( 0 ) )
            On_LeftHold();
        else if ( Input.GetMouseButtonUp( 0 ) )
            On_LeftUp();
        else if ( Input.GetMouseButtonDown( 1 ) )
            On_RightDown();
        else if ( Input.GetMouseButton( 1 ) )
            On_RightHold();
        else if ( Input.GetMouseButtonUp( 1 ) )
            On_RightUp();

        jobHandle.Complete();
        Dependency = jobHandle;
    }

    private void On_LeftDown()
    {
        float3 mousePosition = Input.mousePosition;
        Ray ray = Camera.main.ScreenPointToRay( mousePosition );

        // Start a selection
        if ( Physics.Raycast( ray , out RaycastHit hit ) )
        {
            Debug.Log( "hit" );
            mouseStart = hit.point;
            leftHoldTime = Time.ElapsedTime;

            numClicked++;
            if ( numClicked == 1 )
                doubleClickTime = Time.ElapsedTime;
        }

        Entities.ForEach( ( ref Selected data ) =>
        {
            data.Value = false;
        } ).Run();

        // Check and handle single vs double click
        bool lessTime = Time.ElapsedTime - doubleClickTime < doubleClickDelay;
        bool doubleClick = Time.ElapsedTime - doubleClickTime > doubleClickDelay;

        if ( numClicked > 1 && lessTime )
        {
            numClicked = 0;
            doubleClickTime = 0;
            cameraController.ZoomToLocation( mouseStart );
        }
        else if ( numClicked > 2 || doubleClick )
        {
            numClicked = 0;
        }
    }
    private void On_LeftHold()
    {
        // We are holding so update the selection box
        if ( Time.ElapsedTime - leftHoldTime > leftHoldDelay )
        {
            //GameHandler.Instance.mouse_startPos = mouseStart;
            //GameHandler.Instance.mouse_endPos = Input.mousePosition;
            //GameHandler.Instance.showBox = true;
        }
    }
    private void On_LeftUp()
    {
        float3 mousePosition = Input.mousePosition;
        Ray ray = Camera.main.ScreenPointToRay( mousePosition );

        // Select the units
        if ( Physics.Raycast( ray , out RaycastHit hit ) )
        {
            mousePosition = new float3(
                hit.point.x , hit.point.y , hit.point.z );

            float2 xzSize = new float2(
                math.abs( mousePosition.x - mouseStart.x ) ,
                math.abs( mousePosition.z - mouseStart.z ) );

            // Determine is drag or click select
            if ( xzSize.x > 5f && xzSize.y > 5f )
            {
                float3 startPos = mouseStart;
                float3 endPos = mousePosition;

                // Making sure the startpos really is correct
                if ( startPos.x > endPos.x )
                {
                    float temp = startPos.x;
                    startPos.x = endPos.x;
                    endPos.x = temp;
                }
                if ( startPos.z > endPos.z )
                {
                    float temp = startPos.z;
                    startPos.z = endPos.z;
                    endPos.z = temp;
                }

                DragSelect( startPos , endPos );
            }
            else
            {
                ClickSelect( mousePosition );
            }
        }
    }

    private void On_RightDown()
    {
        float3 mousePosition = Input.mousePosition;
        Ray ray = Camera.main.ScreenPointToRay( mousePosition );

        if ( Physics.Raycast( ray , out RaycastHit hit ) )
            mouseStart = new float3( hit.point.x , hit.point.y , hit.point.z );
    }
    private void On_RightHold()
    {
        float3 mousePosition = Input.mousePosition;
        Ray ray = Camera.main.ScreenPointToRay( mousePosition );

        if ( Physics.Raycast( ray , out RaycastHit hit ) )
        {
            mousePosition = new float3( hit.point.x , hit.point.y , hit.point.z );
            Debug.DrawLine( mouseStart + new float3( 0 , 5 , 0 ) , mousePosition + new float3( 0 , 5 , 0 ) , Color.blue );
        }
    }
    private void On_RightUp()
    {
        float3 mousePosition = Input.mousePosition;
        Ray ray = Camera.main.ScreenPointToRay( mousePosition );

        if ( Physics.Raycast( ray , out RaycastHit hit ) )
        {
            mousePosition = new float3( hit.point.x , hit.point.y , hit.point.z );

            float xSize = math.abs( mousePosition.x - mouseStart.x );
            float zSize = math.abs( mousePosition.z - mouseStart.z );

            entityQuery = GetEntityQuery( ComponentType.ReadOnly( typeof( Selected ) ) );
            var moveJob = new GiveMoveOrdersJob
            {
                mousePosition = mousePosition ,
                selectedHandle = GetComponentTypeHandle<Selected>() ,
                targetHandle = GetComponentTypeHandle<TargetPosition>()
            };
            jobHandle = moveJob.Schedule( entityQuery , Dependency );
        }
    }

    private void ClickSelect( float3 mousePosition )
    {
        entityQuery = GetEntityQuery( ComponentType.ReadOnly( typeof( Selected ) ) );
        var platoonJob = new ClickSelectUnitsJob
        {
            mousePosition = mousePosition ,
            selectedHandle = GetComponentTypeHandle<Selected>() ,
            translationHandle = GetComponentTypeHandle<Translation>() 
        };
        jobHandle = platoonJob.Schedule( entityQuery , Dependency );
    }
    private void DragSelect( float3 boxStart , float3 boxEnd )
    {
        entityQuery = GetEntityQuery( ComponentType.ReadOnly( typeof( Selected ) ) );
        var platoonsJob = new DragSelectUnitsJob
        {
            boxStart = boxStart ,
            boxEnd = boxEnd ,
            selectedHandle = GetComponentTypeHandle<Selected>() ,
            translationHandle = GetComponentTypeHandle<Translation>()
        };
        jobHandle = platoonsJob.Schedule( entityQuery , Dependency );
    }

    #region Jobs

    [BurstCompile]
    private struct ClickSelectUnitsJob : IJobEntityBatch
    {
        [ReadOnly] public float3 mousePosition;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Selected> selectedHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Selected> batchSelected = batchInChunk.GetNativeArray<Selected>( selectedHandle );
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray<Translation>( translationHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                float3 lowerLeft = new float3( batchTranslation[ i ].Value.x - 1 , 0 , batchTranslation[ i ].Value.z - 1 );
                float3 upperRight = new float3( batchTranslation[ i ].Value.x + 1 , 0 , batchTranslation[ i ].Value.z + 1 );

                if ( PointIsWithinRect( mousePosition , lowerLeft , upperRight ) )
                    batchSelected[ i ] = new Selected { Value = true };
            }
        }

        private bool PointIsWithinRect( float3 point , float3 r_lowerLeft , float3 r_upperRight )
        {
            return
            point.x >= r_lowerLeft.x &&
            point.z >= r_lowerLeft.z &&
            point.x <= r_upperRight.x &&
            point.z <= r_upperRight.z;
        }
    }
    [BurstCompile]
    private struct DragSelectUnitsJob : IJobEntityBatch
    {
        [ReadOnly] public float3 boxStart;
        [ReadOnly] public float3 boxEnd;
        [ReadOnly] public ComponentTypeHandle<Translation> translationHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Selected> selectedHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Selected> batchSelected = batchInChunk.GetNativeArray<Selected>( selectedHandle );
            NativeArray<Translation> batchTranslation = batchInChunk.GetNativeArray<Translation>( translationHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                if ( PointIsWithinRect( batchTranslation[ i ].Value , boxStart , boxEnd ) )
                    batchSelected[ i ] = new Selected { Value = true };
            }
        }

        private bool PointIsWithinRect( float3 point , float3 r_lowerLeft , float3 r_upperRight )
        {
            return
            point.x >= r_lowerLeft.x &&
            point.z >= r_lowerLeft.z &&
            point.x <= r_upperRight.x &&
            point.z <= r_upperRight.z;
        }
    }
    [BurstCompile]
    private struct GiveMoveOrdersJob : IJobEntityBatch
    {
        [ReadOnly] public float3 mousePosition;

        [NativeDisableParallelForRestriction] public ComponentTypeHandle<Selected> selectedHandle;
        [NativeDisableParallelForRestriction] public ComponentTypeHandle<TargetPosition> targetHandle;

        public void Execute( ArchetypeChunk batchInChunk , int batchIndex )
        {
            NativeArray<Selected> batchSelected = batchInChunk.GetNativeArray<Selected>( selectedHandle );
            NativeArray<TargetPosition> batchTarget = batchInChunk.GetNativeArray<TargetPosition>( targetHandle );

            for ( int i = 0; i < batchInChunk.Count; i++ )
            {
                if ( batchSelected[ i ].Value )
                {
                    batchTarget[ i ] = new TargetPosition { Value = new float3( mousePosition.x , batchTarget[ i ].Value.y , mousePosition.z ) };
                    batchSelected[ i ] = new Selected { Value = false };
                }
            }
        }
    }

    #endregion
}