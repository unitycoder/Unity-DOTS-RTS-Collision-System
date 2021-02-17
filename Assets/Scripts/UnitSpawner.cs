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

    private const float UNIT_SCALE = 0.5f;
    private const int UNITS_WIDE = 8; // basically how many units wide and long in a group
    private const int UNITS_LONG = 25;
    private const float UNIT_SPACING = 0.5f;
    private const float GROUP_SPACING = 1f;
    private const float GROUP_SIZE_X = ( UNIT_SCALE + UNIT_SPACING ) * UNITS_WIDE;
    private const float GROUP_SIZE_Z = ( UNIT_SCALE + UNIT_SPACING ) * UNITS_LONG;

    public UnitSpawner( Mesh mesh , Material material )
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        unitMesh = mesh;
        unitMaterial = material;

        CreateUnits();
    }

    private void CreateUnits()
    {
        int size = 1;

    
        // call this as many times as you like
        CreateArmy( new POS2D( 10 , 10 ) , size , size );
        CreateArmy( new POS2D( 20 , 10 ) , size , size );
        CreateArmy( new POS2D( 30 , 10 ) , size , size );
        CreateArmy( new POS2D( 40 , 10 ) , size , size );
        CreateArmy( new POS2D( 50 , 10 ) , size , size );
        CreateArmy( new POS2D( 60 , 10 ) , size , size );
        CreateArmy( new POS2D( 70 , 10 ) , size , size );
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
            typeof( Velocity ) ,
            typeof( Mass ) ,
            typeof( CollisionCell ) ,
            typeof( TargetPosition ) ,
            typeof( MoveSpeed ) );

        entityManager.SetComponentData( unit , new Velocity { Value = float3.zero } );
        entityManager.SetComponentData( unit , new Mass { Value = 1f } );
        entityManager.SetComponentData( unit , new TargetPosition { Value = new float3( UnityEngine.Random.Range(5, 100) , 1 , UnityEngine.Random.Range( 5 , 100 ) ) } );
        entityManager.SetComponentData( unit , new MoveSpeed { Walk = 0.4f , Run = 0.8f } );

        entityManager.SetSharedComponentData( unit , new RenderMesh
        {
            mesh = unitMesh ,
            material = unitMaterial
        } );

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
