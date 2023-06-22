using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

public class Manager : MonoBehaviour
{
    public GameObject cameras;
    public GameObject colliders;
    private int activeCameraNo = 0;

    public ComputeShader shader;

    public int texResolution;

    public bool showGrid = true;
    //public bool showSnowParticles = true;
    //public bool toggle = false;
    public Vector3 gridCenter;
    public float cellSize; // m
    private float planeSideSize;
    private Vector3 planeCenter;

    [Range(0.0f, 100.0f)]
    public float timeScale = 0.0f;
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

    public float minSnowTemperature = -20.0f; //degree celcius
    [Range(-20.0f, -1.0f)]
    public float airTemperature = -3.0f; //kg/m^3
    public float groundTemperature = -30.0f; //kg/m^3

    private float time = 0.0f; 

    private int gridWidth;
    private int gridHeight;
    private int gridDepth;

    public Material groundMaterial;
    public Material snowMaterial;
    public Material debugMaterial;
    public Material particleMaterial;
    public Material GridMaterial;

    //TODO: move structs to a separate DataLookup file
    struct Cell
    {
        public Vector3Int gridIndex;
        public Vector3 WSposition;
        public Vector3 pressure;
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
            gridIndex = new Vector3Int(gridX, gridY, gridZ);
            WSposition =  Vector3.zero; //m
            pressure = Vector3.zero;
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
    public struct CollisionData
    {
        public Vector3 position;
        public Vector3 pressure;
    };
    private int collisionCellsCount;

    struct ColumnData
    {
        public float height; //snowHeight
        //public float groundHeight; //offset from plane to ground surface
        public float mass;
        public float mass_temp;
        public float averageDensity;
    };

    struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float size;

        public Particle( float sideSize)
        {
            position = Vector3.zero;
            velocity = Vector3.zero;
            size = sideSize; //radius?
        }
    };
    [Range(0.1f, 10.0f)]
    public float particleSize = 1.0f; // radius
    private int particleCount; // depends on snow height, codependent with particle size

    int SIZE_CELL = 5 * sizeof(int) + 13 * sizeof(float);
    int SIZE_PARTICLE = 7 * sizeof(float);
    int SIZE_COLUMNDATA = 4 * sizeof(float);
    int SIZE_COLLISIONDATA = 6 * sizeof(float);

    Cell[] cellGridArray;
    Particle[] particleArray;
    ColumnData[] snowTotalsArray;
    CollisionData[] collisionsArray; 
    int snowTotalsArraySize;

    ComputeBuffer cellGridBuffer;
    ComputeBuffer particleBuffer;
    ComputeBuffer snowTotalsBuffer;
    ComputeBuffer collisionsBuffer;

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
        InitializeTweakParameters();
        UpdateTweakParameters();
        CreateTextures();
        GenerateHeightMap();
        InitGrid();
        //InitializeColliders();
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
        //shader.SetTexture(kernelInitHeight, "GroundHeightMap", groundHeightMapTexture);
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
        planeCenter = new Vector3(boundsToWorld.x, boundsToWorld.y, boundsToWorld.z);
        shader.SetFloats("planeCenter", new float[] { boundsToWorld.x, boundsToWorld.y, boundsToWorld.z });


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

    private void InitializeTweakParameters()
    {
        // set numerical parameters here only once

        int[] gridDimensions = new int[] { gridWidth, gridHeight, gridDepth };
        shader.SetInts("gridDimensions", gridDimensions); //in cell numbers! 
        shader.SetFloat("cellSize", cellSize);
        shader.SetFloat("maxSnowDensity", maxSnowDensity);
        shader.SetFloat("groundTemperature", groundTemperature);
        shader.SetFloat("V_cell", cellSize * cellSize * cellSize); // m^3

        float[] gridC = new float[] { gridCenter.x, gridCenter.y, gridCenter.z };
        shader.SetFloats("gridCenter", gridC);

        int cellCount = gridWidth * gridHeight * gridDepth;
        shader.SetInt("cellBufferLength", cellCount);

        GridMaterial.SetFloat("_CellSize", cellSize);
        GridMaterial.SetFloat("_MaxSnowDensity", maxSnowDensity);
        GridMaterial.SetFloat("_MinTemperature", minSnowTemperature < groundTemperature? minSnowTemperature: groundTemperature);
    }
    private void UpdateTweakParameters()
    {
        // set numerical parameters that need to be updated every frame

        shader.SetFloat("freshSnowDensity", freshSnowDensity);
        shader.SetFloat("airTemperature", airTemperature);

        shader.SetFloat("h_d_p", h_d_p);
        shader.SetFloat("h_c_p", h_c_p);
        shader.SetFloat("k_d_p", k_d_p);
        shader.SetFloat("k_c_p", k_c_p);

        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetFloat("time", time += Time.deltaTime);
        shader.SetFloat("timeScale", timeScale);

        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;
        shader.SetFloat("snowAddedHeight", snowAddedHeight);
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
                    Cell cell = new Cell(i ,j, k, index, freshSnowDensity, airTemperature);
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

        shader.SetTexture(kernePopulateGrid, "GroundHeightMap", groundHeightMapTexture);
        shader.SetTexture(kernePopulateGrid, "Debug", debugText);


        shader.GetKernelThreadGroupSizes(kernePopulateGrid, out gridThreadGroupSizeX, out gridTthreadGroupSizeY, out gridThreadGroupSizeZ);
        shader.Dispatch(kernePopulateGrid, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX), 
                                           Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY), 
                                           Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

        GridMaterial.SetBuffer("cellGridBuffer", cellGridBuffer);


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
        UpdateTweakParameters();

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
        //UpdateColliders();

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

    void InitializeColliders()
    {
        int colliderCount = colliders.transform.childCount;
        snowTotalsBuffer.GetData(snowTotalsArray);

        for (int i = 0; i < colliderCount; i++)
        {
            SnowCollider collider = colliders.transform.GetChild(i).gameObject.GetComponentInChildren<SnowCollider>();
            collider.cellSize = cellSize;
            int index = WorldPosToArrayIndex(collider.transform.position);
            float height = snowTotalsArray[index].height;
            collider.SetHeight(height);
            collider.Init();
            collisionCellsCount += collider.cellCount;
        }

        collisionsArray = new CollisionData[collisionCellsCount];
        collisionsBuffer = new ComputeBuffer(collisionCellsCount, SIZE_COLLISIONDATA);
        collisionsBuffer.SetData(collisionsArray);

        //set to corresponding kernel
    }

    void UpdateColliders()
    {
        int colliderCount = colliders.transform.childCount;

        for (int i = 0; i < colliderCount; i++)
        {
            SnowCollider collider = colliders.transform.GetChild(i).gameObject.GetComponentInChildren<SnowCollider>();
            collider.cellSize = cellSize;
            int index = WorldPosToArrayIndex(collider.transform.position);
            float snowHeight = snowTotalsArray[index].height;
            //float groundHeight = snowTotalsArray[index].groundHeight;
            collider.SetHeight(snowHeight + /*groundHeight +*/ planeCenter.y);
        }
    }

    int WorldPosToArrayIndex(Vector3 position)
    {
        int index = 0;
        planeCenter = transform.position;

        float mapX = ((-position.x + planeSideSize / 2.0f - planeCenter.x) / (float)planeSideSize);
        float mapY = ((-position.z + planeSideSize / 2.0f - planeCenter.z) / (float)planeSideSize);
        Vector2Int coords = new Vector2Int(Mathf.RoundToInt(mapX * (texResolution - 1.0f)), Mathf.RoundToInt(mapY * (texResolution - 1.0f)));

        index = coords.x + (int) texResolution * coords.y;
        return index;
    }

    void OnGUI()
    {
        float vertical_interval = 35;
        float screep_pos_y_from_top = 35;
        int ui_element_no = 0;
        int screen_width = Screen.width;


        // left screen ui


        timeScale = GUI.HorizontalSlider(new Rect(10, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), timeScale, 0.0f, 100.0f);

        if (GUI.Button(new Rect(10 + 100 + 10, screep_pos_y_from_top + (ui_element_no-1) * vertical_interval, 100, 30), "Start time"))
        {
            timeScale = 0.1f;
            Debug.Log("Time set to " + timeScale);
        }

        if (GUI.Button(new Rect(10, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Get Height"))
        {

            int index = 512 + texResolution * 512;
            float height = snowTotalsArray[index].height;
            Debug.Log("Height of center column 512x512: " + height);
        }

        if (GUI.Button(new Rect(10, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Get Mass"))
        {
            int index = 512 + texResolution * 512;
            float mass = snowTotalsArray[index].mass;
            Debug.Log("Mass of center column 512x512: " + mass);
        }

        snowAddedHeight = GUI.HorizontalSlider(new Rect(10, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), snowAddedHeight, 0.0f, 8.0f);
        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;

        if (GUI.Button(new Rect(10, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Add Snow"))
        {
            shader.SetFloat("snowAddedHeight", snowAddedHeight); 
            shader.SetFloat("airTemperature", airTemperature); 
            shader.Dispatch(kernelAddHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);
            Debug.Log("Adding " + snowAddedHeight + " meters of snow");
        }

        airTemperature = GUI.HorizontalSlider(new Rect(10, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), airTemperature, minSnowTemperature, 0.0f);

        // right screen ui
        ui_element_no = 0;

        if (GUI.Button(new Rect(screen_width - 110, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Switch Camera"))
        {
            int cameraCount = cameras.transform.childCount;
            activeCameraNo++;
            activeCameraNo = activeCameraNo % cameraCount;

            for (int i = 0; i < cameraCount; i++)
            {
                GameObject camera = cameras.transform.GetChild(i).gameObject;
                camera.SetActive(false);
                if (i == activeCameraNo)
                {
                    camera.SetActive(true);
                }
            }
                
        }

        if (GUI.Button(new Rect(screen_width - 110, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Toggle Density"))
        {
            float toggle = GridMaterial.GetFloat("_Show_Density");
            GridMaterial.SetFloat("_Show_Density", toggle == 0.0f ? 1.0f : 0.0f);
        }

        if (GUI.Button(new Rect(screen_width - 110, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Toggle Temperature"))
        {
            float toggle = GridMaterial.GetFloat("_Show_Temperature");
            GridMaterial.SetFloat("_Show_Temperature", toggle == 0.0f ? 1.0f : 0.0f);
        }

        if (GUI.Button(new Rect(screen_width - 110, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 100, 30), "Toggle Pressure"))
        {
            float toggle = GridMaterial.GetFloat("_Show_Pressure");
            GridMaterial.SetFloat("_Show_Pressure", toggle == 0.0f ? 1.0f : 0.0f);
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
