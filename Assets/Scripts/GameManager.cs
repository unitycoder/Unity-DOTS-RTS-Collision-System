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

    }
}
