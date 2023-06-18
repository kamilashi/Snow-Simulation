using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

public class Manager : MonoBehaviour
{
    public ComputeShader shader;

    public int texResolution;

    public bool showGrid = true;
    public bool showSnowParticles = true;
    public bool toggle = false;
    public Vector3 gridCenter;
    public float cellSize; // m
    private float planeSideSize;

    [Range(0.0f, 1000.0f)]
    public float timeScale = 0.5f;
    [Range(0.0f, 1.0f)]
    public float simulationSpeed = 0.5f;
    [Range(0.0f, 8.0f)]
    public float snowAddedHeight = 0.5f;
    public float freshSnowDensity = 20f; //kg/m^3
    public float maxSnowDensity = 30.0f; //kg/m^3

    [Range(0.0f, 10.0f)]
    public float h_d_p = 2.24f;
    [Range(0.0f, 10.0f)]
    public float h_c_p = 2.0f;
    [Range(0.0f, 10.0f)]
    public float k_d_p = 2.56f;
    [Range(0.0f, 10.0f)]
    public float k_c_p = 0.54f; 

    [Range(-20.0f, -1.0f)]
    public float temperature = -3.0f; //kg/m^3
    private float time = 0.0f; 
    //private float k_springCoefficient = 400000000; //N/m^3  //2.8f;

    private int gridWidth;
    private int gridHeight;
    private int gridDepth;

    public Material groundMaterial;
    public Material snowMaterial;
    public Material debugMaterial;
    public Material particleMaterial;
    public Material GridMaterial;

    struct Cell
    {
        public Vector3 gridIndex;
        public Vector3 WSposition;
        public Vector3 force;
        public float density;
        public float indentAmount;
        public float hardness;
        public float temperature;
        public float grainSize;
        public float mass;
        public float massOver;
        public int index;
        public int isOccupied; //TO-DO - enum here

        public Cell(int gridX, int gridY, int gridZ, int cubeIndex, float startDensity, float startTemperature)
        {
            gridIndex = new Vector3(gridX, gridY, gridZ);
            WSposition =  Vector3.zero; //m
            force = Vector3.zero;
            density = startDensity; // kg/m^3
            indentAmount = 0.0f;
            hardness = 0.0f;    // kg/(m*s^2)
            temperature = startTemperature;
            mass = 0.0f;
            massOver = 0.0f;
            grainSize = 0.2f;
            index = cubeIndex;
            isOccupied = 0;
        }
    };
    struct Collider
    {
        public Vector3 velocity;
        public float mass;
        public float speed;
        public float bottomSurfaceArea;
    };

    struct ColumnData
    {
        public float height;
        public float mass;
        public float mass_temp;
        public float averageDensity;
    };

    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float size;
        //public Vector3 force;
        //public Vector3 localPosition;
        //public Vector3 offsetPosition;

        public Particle( float sideSize)
        {
            position = Vector3.zero;
            velocity = Vector3.zero;
            size = sideSize; //radius?
            //force = Vector3.zero;
            //localPosition = Vector3.zero;
            //offsetPosition = Vector3.zero;
        }
    };
    [Range(0.1f, 10.0f)]
    public float particleSize = 1.0f; // radius
    private int particleCount; // depends on snow height, codependent with particle size

    int SIZE_CELL = 5 * sizeof(int) + 13 * sizeof(float);
    int SIZE_PARTICLE = 7 * sizeof(float);
    int SIZE_COLUMNDATA = 4 * sizeof(float);
    Cell[] cellGridArray;
    Particle[] particleArray;
    ColumnData[] snowTotalsArray; // x for height, y for mass...
    int snowTotalsArraySize;

    ComputeBuffer cellGridBuffer;
    ComputeBuffer particleBuffer;
    ComputeBuffer snowTotalsBuffer;

    private uint[] gridArgs;
    private uint[] particleArgs;
    private ComputeBuffer gridArgsBuffer;
    private ComputeBuffer particleArgsBuffer;
    public Mesh cubeMesh
    {
     
        get
        {
           return CjLib.PrimitiveMeshFactory.BoxFlatShaded();
        }
    }
    //public Mesh particleMesh;
    public Mesh particleMesh
    {
        get
        {
            return CjLib.PrimitiveMeshFactory.SphereFlatShaded(6, 6);
        }
    }


    Bounds cellBounds;
    private int kernePopulateGrid;
    private int kernelComputeForces;
    private int kernelApplyForces;
    private int kernelUpdateSnowTotals;
    private int kerneClearGrid;

    uint gridThreadGroupSizeX;
    uint gridTthreadGroupSizeY;
    uint gridThreadGroupSizeZ;

    // Start is called before the first frame update
    void Start()
    {
        InitDefaultArguments();
        CreateTextures();
        GenerateHeightMap();
        InitGrid();
        //InitSnowParticles();
    }

    Renderer rend;
    RenderTexture groundHeightMapTexture;
    RenderTexture snowHeightMapTexture;
    RenderTexture debugText;
    private void CreateTextures()
    {
            groundHeightMapTexture = new RenderTexture(texResolution, texResolution, 0);
            groundHeightMapTexture.enableRandomWrite = true;
            groundHeightMapTexture.Create();

            snowHeightMapTexture = new RenderTexture(texResolution, texResolution, 0);
            snowHeightMapTexture.enableRandomWrite = true;
            snowHeightMapTexture.Create();

            debugText = new RenderTexture(texResolution, texResolution, 0);
            debugText.enableRandomWrite = true;
            debugText.Create();

            rend = GetComponent<Renderer>();
            rend.enabled = true;
            GenerateHeightMap();
    }
    uint heightThreadGroupSizeX;
    uint heightThreadGroupSizeY;
    uint heightThreadGroupSizeZ;
    int kernelGenerateGroundHeight;
    int kernelInitHeight;
    int kernelAddHeight;
    int kernelClearHeight;
    private void GenerateHeightMap()
    {
        snowTotalsArray = new ColumnData[snowTotalsArraySize];
        snowTotalsBuffer = new ComputeBuffer(snowTotalsArraySize, SIZE_COLUMNDATA);
        snowTotalsBuffer.SetData(snowTotalsArray);
        

        kernelGenerateGroundHeight = shader.FindKernel("GenerateHeight");
        kernelInitHeight = shader.FindKernel("InitSnowHeight");
        kernelAddHeight = shader.FindKernel("AddSnowHeight");
        kernelClearHeight = shader.FindKernel("ClearSnowHeight");

        shader.SetInt("texResolution", texResolution);
        shader.SetTexture(kernelGenerateGroundHeight, "GroundHeightMap", groundHeightMapTexture);
        shader.SetTexture(kernelGenerateGroundHeight, "Debug", debugText);

        shader.SetBuffer(kernelInitHeight, "snowTotalsBuffer", snowTotalsBuffer);
        shader.SetBuffer(kernelAddHeight, "snowTotalsBuffer", snowTotalsBuffer);

        
        shader.SetBuffer(kernelClearHeight, "snowTotalsBuffer", snowTotalsBuffer);

        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;
        shader.SetFloat("snowAddedHeight", snowAddedHeight); //important for reconstruction
        groundMaterial.SetTexture("_GroundHeightMap", groundHeightMapTexture);
        snowMaterial.SetBuffer("snowTotalsBuffer", snowTotalsBuffer);
        snowMaterial.SetTexture("_GroundHeightMap", groundHeightMapTexture);

        debugMaterial.SetTexture("_DebugMap", debugText);

        MeshFilter mf = GetComponent<MeshFilter>();
        Bounds bounds = mf.sharedMesh.bounds;

        planeSideSize = Mathf.Abs(bounds.extents.x * 2);
        shader.SetFloat("planeSideSize", planeSideSize);
        Vector3 boundsToWorld = transform.TransformPoint(bounds.center);
        shader.SetFloats("planeCenter", new float[] { boundsToWorld.x, boundsToWorld.y, boundsToWorld.z });
        //Debug.Log("planeCenter y wcc " + boundsToWorld.y);


        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetFloat("timeScale", timeScale);


        shader.GetKernelThreadGroupSizes(kernelGenerateGroundHeight, out heightThreadGroupSizeX, out heightThreadGroupSizeY, out _);
        shader.Dispatch(kernelGenerateGroundHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);

        shader.GetKernelThreadGroupSizes(kernelInitHeight, out heightThreadGroupSizeX, out heightThreadGroupSizeY, out _);
        shader.Dispatch(kernelInitHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);

    }

    private void InitDefaultArguments()
    {
        texResolution = 1024;
        gridWidth = 50;
        gridHeight = 50;
        gridDepth = 50;
        snowTotalsArraySize = texResolution * texResolution;

    }
    private void InitGrid()
    {
        int cellCount = gridWidth * gridHeight * gridDepth;

        Debug.Log("cellCount " + cellCount);


        cellGridArray = new Cell[cellCount];
        int index = 0;
        for (int i =0;i< gridWidth; i++)
        {
            for (int j = 0; j < gridHeight; j++)
            {
                for (int k = 0; k < gridDepth; k++)
                {
                    Cell cell = new Cell(k,j,i, index, freshSnowDensity, temperature);
                    cellGridArray[index] = cell;
                    index++;
                }
            }
        }

        cellGridBuffer = new ComputeBuffer(cellCount, SIZE_CELL);
        cellGridBuffer.SetData(cellGridArray);

        kernePopulateGrid = shader.FindKernel("PopulateGrid");
        shader.SetBuffer(kernePopulateGrid, "cellGridBuffer", cellGridBuffer);
        shader.SetBuffer(kernePopulateGrid, "snowTotalsBuffer", snowTotalsBuffer);

        kerneClearGrid = shader.FindKernel("ClearGrid");
        shader.SetBuffer(kerneClearGrid, "cellGridBuffer", cellGridBuffer);

        int[] gridDimensions = new int[] { gridWidth, gridHeight, gridDepth };
        shader.SetInts( "gridDimensions", gridDimensions); //in cell numbers! 
        shader.SetFloat("cellSize", cellSize);
        shader.SetFloat("maxSnowDensity", maxSnowDensity);
        shader.SetFloat("freshSnowDensity", freshSnowDensity);
        shader.SetFloat("temperature", temperature);
        
        shader.SetFloat("h_d_p", h_d_p);
        shader.SetFloat("h_c_p", h_c_p);
        shader.SetFloat("k_d_p", k_d_p);
        shader.SetFloat("k_c_p", k_c_p);

        //shader.SetFloat("k_springCoefficient", k_springCoefficient); 
        shader.SetFloat("V_cell", cellSize* cellSize* cellSize); // m^3
        float[] gridC = new float[] { gridCenter.x, gridCenter.y, gridCenter.z };
        shader.SetFloats("gridCenter", gridC);
        shader.SetInt("cellBufferLength", cellCount);
        shader.SetTexture(kernePopulateGrid, "GroundHeightMap", groundHeightMapTexture);
        shader.SetTexture(kernePopulateGrid, "Debug", debugText);

        shader.GetKernelThreadGroupSizes(kernePopulateGrid, out gridThreadGroupSizeX, out gridTthreadGroupSizeY, out gridThreadGroupSizeZ);
        shader.Dispatch(kernePopulateGrid, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX), 
                                           Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY), 
                                           Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

        GridMaterial.SetBuffer("cellGridBuffer", cellGridBuffer);
        GridMaterial.SetFloat("_CellSize", cellSize);
        GridMaterial.SetFloat("_MaxSnowDensity", maxSnowDensity);

        gridArgs = new uint[] { cubeMesh.GetIndexCount(0), (uint)cellCount, 0, 0, 0 };
        gridArgsBuffer = new ComputeBuffer(1, gridArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        gridArgsBuffer.SetData(gridArgs);
        cellBounds = new Bounds(Vector3.zero, new Vector3(30, 30, 30)); //place holder

        kernelComputeForces = shader.FindKernel("ComputeForces");
        shader.SetBuffer(kernelComputeForces, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelComputeForces, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelComputeForces, "snowTotalsBuffer", snowTotalsBuffer);

        kernelApplyForces = shader.FindKernel("ApplyForces");
        shader.SetBuffer(kernelApplyForces, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelApplyForces, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelApplyForces, "snowTotalsBuffer", snowTotalsBuffer);

        kernelUpdateSnowTotals = shader.FindKernel("UpdateSnowTotals");
        shader.SetBuffer(kernelUpdateSnowTotals, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelUpdateSnowTotals, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelUpdateSnowTotals, "snowTotalsBuffer", snowTotalsBuffer);
    }
        // Update is called once per frame
        void Update()
    {
        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetFloat("time", time += Time.deltaTime);
        shader.SetFloat("timeScale", timeScale);
        shader.SetFloat("simulationSpeed", simulationSpeed);

        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;
        shader.SetFloat("snowAddedHeight", snowAddedHeight); 
        shader.SetFloat("freshSnowDensity", freshSnowDensity); 
        shader.SetFloat("temperature", temperature);
        shader.SetFloat("maxSnowDensity", maxSnowDensity);
        shader.SetFloat("h_d_p", h_d_p);
        shader.SetFloat("h_c_p", h_c_p);
        shader.SetFloat("k_d_p", k_d_p);
        shader.SetFloat("k_c_p", k_c_p);
        GridMaterial.SetFloat("maxSnowDensity", maxSnowDensity);

        shader.Dispatch(kernelClearHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);

        shader.GetKernelThreadGroupSizes(kernelGenerateGroundHeight, out heightThreadGroupSizeX, out heightThreadGroupSizeY, out heightThreadGroupSizeZ);
        shader.Dispatch(kernePopulateGrid, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
                                              Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
                                              Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));



        //shader.Dispatch(kernelComputeForces, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
        //                                      Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
        //                                      Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));
        
        shader.Dispatch(kernelComputeForces, 50, 1, 50);
        shader.Dispatch(kernelApplyForces, 50, 1, 50);


        //shader.Dispatch(kernelApplyForces, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
        //                                     Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
        //                                      Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));



        shader.Dispatch(kernelUpdateSnowTotals, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX),
                                             Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY),
                                              gridHeight);

        snowTotalsBuffer.GetData(snowTotalsArray);


        //no lagrangian particles for now:
        //shader.Dispatch(kernelCollisionDetection, Mathf.CeilToInt((float)particlesPerAxis.x / (float)snowThreadGroupSizeX),
        //                                   Mathf.CeilToInt((float)particlesPerAxis.y / (float)snowTthreadGroupSizeY),
        //                                   Mathf.CeilToInt((float)particlesPerAxis.z / (float)snowThreadGroupSizeZ));

        //shader.Dispatch(kernelPropagateForce, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
        //                                   Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
        //                                   Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

        //shader.Dispatch(kernelUpdateParticleVelocity, Mathf.CeilToInt((float)particlesPerAxis.x / (float)snowThreadGroupSizeX),
        //                                   Mathf.CeilToInt((float)particlesPerAxis.y / (float)snowTthreadGroupSizeY),
        //                                   Mathf.CeilToInt((float)particlesPerAxis.z / (float)snowThreadGroupSizeZ));
        //shader.Dispatch(kernelUpdateParticlePosition, Mathf.CeilToInt((float)particlesPerAxis.x / (float)snowThreadGroupSizeX),
        //                                   Mathf.CeilToInt((float)particlesPerAxis.y / (float)snowTthreadGroupSizeY),
        //                                   Mathf.CeilToInt((float)particlesPerAxis.z / (float)snowThreadGroupSizeZ));
        //shader.Dispatch(kernelDissipateForces, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
        //                                       Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
        //                                       Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

        //DebugPrint();
        if (showGrid)
        {
            Graphics.DrawMeshInstancedIndirect(cubeMesh, 0, GridMaterial, cellBounds, gridArgsBuffer);
        }



        //shader.GetKernelThreadGroupSizes(kerneClearGrid, out gridThreadGroupSizeX, out gridTthreadGroupSizeY, out gridThreadGroupSizeZ);
        shader.Dispatch(kerneClearGrid, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
                                          Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
                                           Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

        //if(showSnowParticles)
        //{
        //    Graphics.DrawMeshInstancedIndirect(particleMesh, 0, particleMaterial, particleBounds, particleArgsBuffer);
        //}
    }

    void OnDestroy()
    {
        cellGridBuffer.Release();

        if (gridArgsBuffer != null)
        {

            //Debug.Log("args buffer released " + argsBuffer);
            gridArgsBuffer.Release();
        }

        snowTotalsBuffer.Release();

        //particleBuffer.Release();

        if (particleArgsBuffer != null)
        {
            //Debug.Log("args buffer released " + argsBuffer);
            particleArgsBuffer.Release();
        }
    }

    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 35, 65, 30), "Print Height"))
        {
            
            int index = 512 + 1024 * 512;
            float height = snowTotalsArray[index].height;
           Debug.Log("Height of center column 512x512: " +  height);
        }

        if(GUI.Button(new Rect(10, 70, 65, 30), "Print Mass"))
        {
            int index = 512 + 1024 * 512;
            float mass = snowTotalsArray[index].mass;
            Debug.Log("Mass of center column 512x512: " + mass);
        }

        if (GUI.Button(new Rect(10, 105, 65, 30), "Add Snow"))
        {
            shader.Dispatch(kernelAddHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);
            Debug.Log("Adding " + snowAddedHeight + " meters of snow");
        }
    }

    void DebugPrint()
    {
        int index = 0;
        for (int i = 0; i < gridWidth; i++)
        {
            for (int j = 0; j < gridHeight; j++)
            {
                for (int k = 0; k < gridDepth; k++)
                {

                    Debug.Log("WS position: " + cellGridArray[index].WSposition);
                    index++;
                }
            }
        }
    }
}
