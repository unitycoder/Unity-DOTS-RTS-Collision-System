using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class UnitSpawner
{
    private EntityManager entityManager;

    private Mesh unitMesh;
    private Material unitMaterial;

    // THIS TOGGLES WETHER ALL UNITS WILL BE GIVEN A RANDOM POSITION TO MOVE TO AT START
    // IF SET TO FALSE, USE MOUSE TO DRAG/CLICK SELECT UNITS THEN RIGHT CLICK TO SEND THEM TO A POSITION
    private bool RANDOM_POSITION = true;

    // Just unit spacing data, units spawned in "groups" withing "armies", which are groups of groups
    private const float UNIT_SCALE = 0.5f;
    private const float UNIT_SPACING = 0.25f;
    private const float GROUP_SPACING = 1f;
    private const float GROUP_SIZE_X = ( UNIT_SCALE + UNIT_SPACING ) * UNITS_WIDE;
    private const float GROUP_SIZE_Z = ( UNIT_SCALE + UNIT_SPACING ) * UNITS_LONG;

    // CHANGE THESE TO ALTER NUMBER OF UNITS SPAWNED
    private const int UNITS_WIDE = 8;
    private const int UNITS_LONG = 25;
    private const int GROUPS_LONG = 1;
    private const int GROUPS_WIDE = 1;

    public UnitSpawner( Mesh mesh , Material material )
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        unitMesh = mesh;
        unitMaterial = material;

        CreateUnits();
    }

    private void CreateUnits()
    {
        for ( int i = 0; i < 17; i++ )
            for ( int j = 0; j < 17; j++ )
                CreateArmy( new POS2D( 10 * i + 20 , 20 * j + 20 ) , GROUPS_WIDE , GROUPS_LONG );
    }
    private void CreateArmy( POS2D pos , int sizeX , int sizeZ )
    {
        for ( int z = 0; z < sizeZ; z++ )
        {
            POS2D groupPos = new POS2D( pos.x , pos.z + GROUP_SIZE_Z * z + GROUP_SPACING );

            for ( int x = 0; x < sizeX; x++ )
            {
                groupPos = new POS2D( groupPos.x + GROUP_SIZE_X * x + GROUP_SPACING , groupPos.z );
                CreateGroup( groupPos );
            }
        }
    }
    private void CreateGroup( POS2D startPos2D )
    {
        for ( int z = 0; z < UNITS_LONG; z++ )
        {
            for ( int x = 0; x < UNITS_WIDE; x++ )
            {
                float xPos = startPos2D.x + x * ( UNIT_SCALE + UNIT_SPACING );
                float zPos = startPos2D.z + z * ( UNIT_SCALE + UNIT_SPACING );
                POS2D unitPosition = new POS2D( xPos , zPos );

                CreateUnit( unitPosition );
            }
        }
    }
    private void CreateUnit( POS2D pos2D )
    {
        Entity unit = entityManager.CreateEntity(
            typeof( UnitTag ) ,

            // Render
            typeof( RenderMesh ) ,
            typeof( RenderBounds ) ,
            typeof( LocalToWorld ) ,

            typeof( Translation ) ,
            typeof( Rotation ) ,
            typeof( Scale ) ,

            typeof( MoveForce ) ,
            typeof( Drag ) ,
            typeof( Mass ) ,

            typeof( Velocity ) ,
            typeof( Direction ) ,
            typeof( TargetPosition ) ,
            typeof( Selected ) ,
            typeof( Moving ) ,

            typeof( CollisionCell ) ,
            typeof( CollisionCellMulti ) );

        entityManager.SetComponentData( unit , new Velocity { Value = float3.zero } );
        entityManager.SetComponentData( unit , new Mass { Value = 1f } );

        if (RANDOM_POSITION)
            entityManager.SetComponentData( unit , new TargetPosition { Value = new float3( UnityEngine.Random.Range( 5 , 5000 ) , 1 , UnityEngine.Random.Range( 5 , 5000 ) ) } );
        else
            entityManager.SetComponentData( unit , new TargetPosition { Value = new float3( pos2D.x + 1 , 1 , pos2D.z + 1) } );

        entityManager.SetComponentData( unit , new Selected { Value = false } );
        entityManager.SetComponentData( unit , new Moving { Value = -1 } );
        entityManager.SetComponentData( unit , new MoveForce { Value = UnityEngine.Random.Range( 10f , 35f ) } );
        entityManager.SetComponentData( unit , new Drag { Value = 1.05f } );
        entityManager.SetComponentData( unit , new Direction { Value = new float3( 0 , 0 , 0 ) } );

        /*entityManager.SetSharedComponentData( unit , new RenderMesh
        {
            mesh = unitMesh ,
            material = unitMaterial
        } );*/

        entityManager.SetComponentData( unit , new Translation { Value = new float3( pos2D.x , 1 , pos2D.z ) } );
        entityManager.SetComponentData( unit , new Scale { Value = UNIT_SCALE } );
    }

    private struct POS2D
    {
        public float x;
        public float z;

        public POS2D( float _x , float _z )
        {
            x = _x;
            z = _z;
        }
    }
}
