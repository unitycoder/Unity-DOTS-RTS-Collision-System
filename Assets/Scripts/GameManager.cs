using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Rendering;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;

public class GameManager : MonoBehaviour
{
    int[] num;
    int mapLength = 2000;

    public Mesh unitMesh;
    public Material unitMaterial;

    public UnitSpawner unitSpawner;

    // Start is called before the first frame update
    void Start()
    {
        unitSpawner = new UnitSpawner( unitMesh , unitMaterial );
    }

    // Update is called once per frame
    void Update()
    {
        //DrawGrid();
    }

    private void DrawGrid()
    {
        for ( int i = 0; i < 100; i++ )
        {
            for ( int j = 0; j < 100; j++ )
            {
                Vector3 start = new Vector3( j , 0.1f , i );
                Vector3 end = new Vector3( j , 0.1f , i + 100 );

                Debug.DrawLine( start , end , Color.blue );
            }
        }
        for ( int i = 0; i < 100; i++ )
        {
            for ( int j = 0; j < 100; j++ )
            {
                Vector3 start = new Vector3( i , 0.1f , j );
                Vector3 end = new Vector3( i + 100 , 0.1f , j );

                Debug.DrawLine( start , end , Color.blue );
            }
        }
    }
}
