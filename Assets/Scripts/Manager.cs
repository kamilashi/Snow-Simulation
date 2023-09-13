using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
public class Manager : MonoBehaviour
{
    // for switching the scene cameras:
    public GameObject cameras;
    public GameObject colliders;
    private int activeCameraNo = 0;

    // main compute shader:
    public ComputeShader shader;

    // height map parameters:
    public int texResolution;

    // grid parameters:
    int gridWidth;
    int gridHeight;
    int gridDepth;
    public bool showGrid = true;
    public Vector3 gridCenter;
    public float cellSize; // in meters
    float planeSideSize;
    Vector3 planeCenter;

    // simulation parameters:
    [Range(0.0f, 100.0f)]
    public float timeScale = 0.0f;
    [Range(0.0f, 8.0f)]
    public float snowAddedHeight = 6.0f;
    public float freshSnowDensity = 20f; //kg/m^3
    public float maxSnowDensity = 100.0f; //kg/m^3
    [Range(0.0f, 10.0f)]
    public float kN = 3.56f;
    public float minSnowTemperature = -20.0f; //degree celcius
    [Range(-20.0f, -1.0f)]
    public float airTemperature = -3.0f; //kg/m^3
    public float groundTemperature = -30.0f; //kg/m^3
    private float elapsedSimulationTime = 0.0f;

    // UI related
    private float centralSnowColumnHeight = 0;
    private float ñentralSnowColumnMass = 0; 
    private float elapsedTime = 0.0f;
    private int frameCount = 0;

    // rendering related:
    Renderer rend;
    RenderTexture groundHeightMapTexture;
    RenderTexture snowHeightMapTexture;
    public Material groundMaterial;
    public Material snowMaterial;
    public Material GridMaterial;

    // compute shader related:
    int SIZE_CELL = 5 * sizeof(int) + 17 * sizeof(float);
    int SIZE_COLUMNDATA = 4 * sizeof(float);
    int SIZE_COLLISIONDATA = 6 * sizeof(float);

    Cell[] cellGridArray;
    ColumnData[] snowColumnsArray;
    CollisionData[] collisionsArray;
    int snowColumnsCount;
    int collisionCellsCount;
    int gridCellCount;

    ComputeBuffer cellGridBuffer;
    ComputeBuffer snowColumnsBuffer;
    ComputeBuffer collisionsBuffer;

    private uint[] gridArgs;
    private ComputeBuffer gridArgsBuffer;

    Bounds cellBounds;

    int kernelGenerateGroundHeight;
    int kernelInitSnowColumns;
    int kernelAddHeight;
    int kernelClearSnowColumns;
    int kernelSetPressure;
    int kernePopulateGrid;
    int kernelComputePressures;
    int kernelApplyPressures;
    int kernelResampleDensity;
    int kernelUpdateSnowColumns;
    int kerneClearGrid;

    uint gridThreadGroupSizeX;
    uint gridTthreadGroupSizeY;
    uint gridThreadGroupSizeZ;
    Vector3Int gridThreadCalls;
    uint heightThreadGroupSizeX;
    uint heightThreadGroupSizeY;
    Vector3Int heightMapThreadCalls;

    // data structs:
    struct Cell
    {
        public Vector3Int gridIndex;
        public int index;
        public Vector3 WSposition;
        public float indentAmount;
        public float xCompressionAmount;
        public Vector3 pressure;
        public float hardness;
        public Vector3 appliedPressure;
        public float density;
        public float temperature;
        public float grainSize;
        public float mass;
        public float massOver;
        public int isOccupied;
        public Cell(int gridX, int gridY, int gridZ, int cubeIndex, float startDensity, float startTemperature)
        {
            gridIndex = new Vector3Int(gridX, gridY, gridZ);
            WSposition = Vector3.zero; // m
            pressure = Vector3.zero; // Pa
            appliedPressure = Vector3.zero; // Pa
            density = startDensity; // kg/m^3
            indentAmount = 0.0f;
            xCompressionAmount = 0.0f;
            hardness = 0.0f;    // Pa
            temperature = startTemperature;
            mass = 0.0f; // kg
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

    struct ColumnData
    {
        public float height; //snowHeight
        public float groundHeight; //offset from plane to ground surface
        public float mass;
        public float mass_temp;
    };

    public Mesh cubeMesh
    {
        get
        {
            return CjLib.PrimitiveMeshFactory.BoxFlatShaded();
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        InitDefaultArguments();
        InitializeTweakParameters();
        UpdateTweakParameters();
        CreateTextures();
        GenerateHeightMap();
        InitGrid();
        InitializeColliders();
        UpdateColliders();
    }

    private void CreateTextures()
    {
        groundHeightMapTexture = new RenderTexture(texResolution, texResolution, 0);
        groundHeightMapTexture.enableRandomWrite = true;
        groundHeightMapTexture.Create();

        snowHeightMapTexture = new RenderTexture(texResolution, texResolution, 0);
        snowHeightMapTexture.enableRandomWrite = true;
        snowHeightMapTexture.Create();

        rend = GetComponent<Renderer>();
        rend.enabled = true;
        GenerateHeightMap();
    }

    private void GenerateHeightMap()
    {
        snowColumnsArray = new ColumnData[snowColumnsCount];
        snowColumnsBuffer = new ComputeBuffer(snowColumnsCount, SIZE_COLUMNDATA);
        snowColumnsBuffer.SetData(snowColumnsArray);

        kernelGenerateGroundHeight = shader.FindKernel("GenerateHeight");
        kernelInitSnowColumns = shader.FindKernel("InitSnowColumns");
        kernelAddHeight = shader.FindKernel("AddSnowHeight");
        kernelClearSnowColumns = shader.FindKernel("ClearSnowColumns");

        shader.SetInt("texResolution", texResolution);
        shader.SetTexture(kernelGenerateGroundHeight, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelGenerateGroundHeight, "snowColumnsBuffer", snowColumnsBuffer);
        shader.SetBuffer(kernelInitSnowColumns, "snowColumnsBuffer", snowColumnsBuffer);
        shader.SetBuffer(kernelAddHeight, "snowColumnsBuffer", snowColumnsBuffer);
        shader.SetBuffer(kernelClearSnowColumns, "snowColumnsBuffer", snowColumnsBuffer);

        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;
        shader.SetFloat("snowAddedHeight", snowAddedHeight);
        groundMaterial.SetTexture("_GroundHeightMap", groundHeightMapTexture);
        snowMaterial.SetBuffer("snowColumnsBuffer", snowColumnsBuffer);
        snowMaterial.SetTexture("_GroundHeightMap", groundHeightMapTexture);
        snowMaterial.SetFloat("_SnowMaxHeight", snowAddedHeight);
        snowMaterial.SetInteger("_TexResolution", texResolution);

        // handle plane rendering: 
        MeshFilter mf = GetComponent<MeshFilter>();
        Bounds bounds = mf.sharedMesh.bounds;
        planeSideSize = Mathf.Abs(bounds.extents.x * 2) * transform.localScale.x;
        shader.SetFloat("planeSideSize", planeSideSize);
        Vector3 boundsToWorld = transform.TransformPoint(bounds.center);
        planeCenter = new Vector3(boundsToWorld.x, boundsToWorld.y, boundsToWorld.z);
        shader.SetFloats("planeCenter", new float[] { boundsToWorld.x, boundsToWorld.y, boundsToWorld.z });

        shader.GetKernelThreadGroupSizes(kernelGenerateGroundHeight, out heightThreadGroupSizeX, out heightThreadGroupSizeY, out _);
        heightMapThreadCalls.x = Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX);
        heightMapThreadCalls.y = Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY);
        heightMapThreadCalls.z = 1;

        shader.Dispatch(kernelGenerateGroundHeight, heightMapThreadCalls.x, heightMapThreadCalls.y, heightMapThreadCalls.z);
        shader.Dispatch(kernelInitSnowColumns, heightMapThreadCalls.x, heightMapThreadCalls.y, heightMapThreadCalls.z);
    }

    private void InitDefaultArguments()
    {
        texResolution = 1024;
        gridWidth = 50;
        gridHeight = 50;
        gridDepth = 50;
        snowColumnsCount = texResolution * texResolution;
        timeScale = 0.0f;
        elapsedSimulationTime = 0.0f;
    }
    private void InitializeTweakParameters()
    {
        int[] gridDimensions = new int[] { gridWidth, gridHeight, gridDepth };
        shader.SetInts("gridDimensions", gridDimensions); //in cell numbers
        shader.SetFloat("cellSize", cellSize);
        shader.SetFloat("maxSnowDensity", maxSnowDensity);
        shader.SetFloat("groundTemperature", groundTemperature);
        shader.SetFloat("V_cell", cellSize * cellSize * cellSize); // m^3
        float[] gridC = new float[] { gridCenter.x, gridCenter.y, gridCenter.z };
        shader.SetFloats("gridCenter", gridC);
        gridCellCount = gridWidth * gridHeight * gridDepth;
        shader.SetInt("cellBufferLength", gridCellCount);

        GridMaterial.SetFloat("_CellSize", cellSize);
        GridMaterial.SetFloat("_MaxSnowDensity", maxSnowDensity);
        GridMaterial.SetFloat("_MinTemperature", minSnowTemperature < groundTemperature ? minSnowTemperature : groundTemperature);
    }
    private void UpdateTweakParameters()
    {
        shader.SetFloat("freshSnowDensity", freshSnowDensity);
        shader.SetFloat("airTemperature", airTemperature);
        shader.SetFloat("kN", kN);

        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetFloat("timeScale", timeScale);
        shader.SetFloat("time", elapsedSimulationTime += Time.deltaTime * timeScale);
        elapsedTime += Time.deltaTime;

        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;
        shader.SetFloat("snowAddedHeight", snowAddedHeight);
    }
    private void InitGrid()
    {
        cellGridArray = new Cell[gridCellCount];
        int index = 0;
        for (int i = 0; i < gridWidth; i++)
        {
            for (int j = 0; j < gridHeight; j++)
            {
                for (int k = 0; k < gridDepth; k++)
                {
                    Cell cell = new Cell(i, j, k, index, freshSnowDensity, airTemperature);
                    cellGridArray[index] = cell;
                    index++;
                }
            }
        }
        cellGridBuffer = new ComputeBuffer(gridCellCount, SIZE_CELL);
        cellGridBuffer.SetData(cellGridArray);

        kernePopulateGrid = shader.FindKernel("PopulateGrid");
        shader.SetBuffer(kernePopulateGrid, "cellGridBuffer", cellGridBuffer);
        shader.SetBuffer(kernePopulateGrid, "snowColumnsBuffer", snowColumnsBuffer);
        shader.SetTexture(kernePopulateGrid, "GroundHeightMap", groundHeightMapTexture);

        kerneClearGrid = shader.FindKernel("ClearGrid");
        shader.SetBuffer(kerneClearGrid, "cellGridBuffer", cellGridBuffer);

        shader.GetKernelThreadGroupSizes(kernePopulateGrid, out gridThreadGroupSizeX, out gridTthreadGroupSizeY, out gridThreadGroupSizeZ);
        gridThreadCalls.x = Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX);
        gridThreadCalls.y = Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY);
        gridThreadCalls.z = Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ);
        shader.Dispatch(kernePopulateGrid, gridThreadCalls.x, gridThreadCalls.y, gridThreadCalls.z);

        GridMaterial.SetBuffer("cellGridBuffer", cellGridBuffer);

        gridArgs = new uint[] { cubeMesh.GetIndexCount(0), (uint)gridCellCount, 0, 0, 0 };
        gridArgsBuffer = new ComputeBuffer(1, gridArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        gridArgsBuffer.SetData(gridArgs);
        cellBounds = new Bounds(Vector3.zero, new Vector3(30, 30, 30)); 

        kernelComputePressures = shader.FindKernel("ComputeForces");
        shader.SetBuffer(kernelComputePressures, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelComputePressures, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelComputePressures, "snowColumnsBuffer", snowColumnsBuffer);

        kernelApplyPressures = shader.FindKernel("ApplyForces");
        shader.SetBuffer(kernelApplyPressures, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelApplyPressures, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelApplyPressures, "snowColumnsBuffer", snowColumnsBuffer);

        kernelResampleDensity = shader.FindKernel("ResampleDensity");
        shader.SetBuffer(kernelResampleDensity, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelResampleDensity, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelResampleDensity, "snowColumnsBuffer", snowColumnsBuffer);

        kernelUpdateSnowColumns = shader.FindKernel("UpdateSnowColumns");
        shader.SetBuffer(kernelUpdateSnowColumns, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelUpdateSnowColumns, "GroundHeightMap", groundHeightMapTexture);
        shader.SetBuffer(kernelUpdateSnowColumns, "snowColumnsBuffer", snowColumnsBuffer);
    }
    // Update is called once per frame
    void Update()
    {
        UpdateTweakParameters();
        shader.Dispatch(kernelClearSnowColumns, heightMapThreadCalls.x, heightMapThreadCalls.y, heightMapThreadCalls.z);
        shader.Dispatch(kernePopulateGrid, gridThreadCalls.x, gridThreadCalls.y, gridThreadCalls.z);
        if (colliders.activeSelf) shader.Dispatch(kernelSetPressure, collisionCellsCount, 1, 1);
        shader.Dispatch(kernelComputePressures, 2, 1, 2);
        shader.Dispatch(kernelApplyPressures, 5, 5, 5);
        shader.Dispatch(kernelResampleDensity, 2, 1, 2);
        shader.Dispatch(kernelUpdateSnowColumns, 32, 32, 1);
        StartCoroutine(GetColumnDataFromGPU());
        if (colliders.activeSelf) UpdateColliders();
        if (showGrid) Graphics.DrawMeshInstancedProcedural(cubeMesh, 0, GridMaterial, cellBounds, gridCellCount);
        shader.Dispatch(kerneClearGrid, gridThreadCalls.x, gridThreadCalls.y, gridThreadCalls.z);
    }

    void OnDestroy()
    {
        if (gridArgsBuffer != null) gridArgsBuffer.Dispose();
        cellGridBuffer.Dispose();
        snowColumnsBuffer.Dispose();
        collisionsBuffer.Dispose();
    }

    void InitializeColliders()
    {
        int colliderCount = colliders.transform.childCount;
        for (int i = 0; i < colliderCount; i++)
        {
            SnowCollider collider = colliders.transform.GetChild(i).gameObject.GetComponentInChildren<SnowCollider>();
            collider.cellSize = cellSize;
            int index = WorldPosToArrayIndex(collider.transform.position);
            float height = snowColumnsArray[index].height;
            collider.SetHeight(height);
            collider.Initialize();
            collisionCellsCount += collider.cellCount;
        }
        collisionsArray = new CollisionData[collisionCellsCount];
        collisionsBuffer = new ComputeBuffer(collisionCellsCount, SIZE_COLLISIONDATA);
        collisionsBuffer.SetData(collisionsArray);
        kernelSetPressure = shader.FindKernel("SetPressure");
        shader.SetBuffer(kernelSetPressure, "collisionsBuffer", collisionsBuffer);
        shader.SetBuffer(kernelSetPressure, "cellGridBuffer", cellGridBuffer);
    }

    private IEnumerator GetColumnDataFromGPU()
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(snowColumnsBuffer);
        while (!request.done)
        {
            yield return null;
        }
        if (!request.hasError) snowColumnsArray = request.GetData<ColumnData>().ToArray();
    }

   private float GetFPS()
    {
        frameCount++;
        float fps = frameCount / elapsedTime;
        return fps;
    } 

    void UpdateColliders()
    {
        int colliderCount = colliders.transform.childCount;
        int head_index = 0;
        for (int i = 0; i < colliderCount; i++)
        {
            SnowCollider collider = colliders.transform.GetChild(i).gameObject.GetComponentInChildren<SnowCollider>();
            collider.cellSize = cellSize;
            int index = WorldPosToArrayIndex(collider.transform.position);
            float snowHeight = snowColumnsArray[index].height;
            float groundHeight = snowColumnsArray[index].groundHeight;
            collider.SetHeight(snowHeight + groundHeight + planeCenter.y);
            collider.getCollisionData().CopyTo(collisionsArray, head_index);
            head_index += collider.cellCount;
        }
        collisionsBuffer.SetData(collisionsArray);
    }

    int WorldPosToArrayIndex(Vector3 position)
    {
        planeCenter = transform.position;
        float mapX = ((-position.x + planeSideSize / 2.0f - planeCenter.x) / (float)planeSideSize);
        float mapY = ((-position.z + planeSideSize / 2.0f - planeCenter.z) / (float)planeSideSize);
        Vector2Int coords = new Vector2Int(Mathf.RoundToInt(mapX * (texResolution - 1.0f)), Mathf.RoundToInt(mapY * (texResolution - 1.0f)));
        int index = coords.x + (int)texResolution * coords.y;
        return index;
    }

    [ExecuteInEditMode]
    void OnGUI()
    {
        float element_width = 100;
        float element_height = 30;
        float vertical_interval = 35;
        float horizontal_interval = 10;
        float screep_pos_y_from_top = 35;
        int ui_element_no = 0;
        int screen_width = Screen.width;

        // left screen ui
        GUI.Label(new Rect(horizontal_interval + element_width + horizontal_interval, screep_pos_y_from_top + (ui_element_no) * vertical_interval, element_width, element_height), "Time scale");
        GUI.Label(new Rect((screen_width - element_width) / 3, screep_pos_y_from_top + (ui_element_no) * vertical_interval, 3.0f * element_width, element_height), "Simulation time: " + elapsedSimulationTime);
        timeScale = GUI.HorizontalSlider(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), timeScale, 0.0f, element_width);
        if (GUI.Button(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no * vertical_interval, element_width, element_height), "Start time"))
        {
            timeScale = 1.0f;
        }
        if (GUI.Button(new Rect(horizontal_interval + element_width + horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Stop time"))
        {
            timeScale = 0.0f;
        }
        GUI.Label(new Rect(horizontal_interval + element_width + horizontal_interval, screep_pos_y_from_top + ui_element_no * vertical_interval, element_width, element_height * 2.0f), "Snow height to add: " + snowAddedHeight + " m.");
        snowAddedHeight = GUI.HorizontalSlider(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), snowAddedHeight, 0.0f, 8.0f);
        snowAddedHeight = snowAddedHeight - snowAddedHeight % cellSize;
        if (GUI.Button(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Add Snow"))
        {
            shader.SetFloat("snowAddedHeight", snowAddedHeight);
            shader.SetFloat("airTemperature", airTemperature);
            shader.Dispatch(kernelAddHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);
        }

        GUI.Label(new Rect(horizontal_interval + element_width + horizontal_interval, screep_pos_y_from_top + ui_element_no * vertical_interval, element_width, element_height * 2.0f), "Air temperature: " + airTemperature + " deg. C");
        airTemperature = GUI.HorizontalSlider(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), airTemperature, minSnowTemperature, 0.0f);
        airTemperature = Mathf.Round(airTemperature);

        if (GUI.Button(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Reset"))
        {
            Reset();
        }

        if (GUI.Button(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Toggle Colliders"))
        {
            ToggleColliders();
        }

        if (GUI.Button(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no * vertical_interval, element_width, element_height), "Get Height"))
        {
            int index = 512 + texResolution * 512;
            centralSnowColumnHeight = snowColumnsArray[index].height;
        }
        GUI.Label(new Rect(horizontal_interval + element_width + horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 2 *element_width, element_height * 2.0f), "Last central column \n height update: " + centralSnowColumnHeight + " m");

        if (GUI.Button(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no * vertical_interval, element_width, element_height), "Get Mass"))
        {
            int index = 512 + texResolution * 512;
            ñentralSnowColumnMass = snowColumnsArray[index].mass;
        }
        GUI.Label(new Rect(horizontal_interval + element_width + horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 2 * element_width, element_height * 2.0f), "Last central column \n mass update: " + ñentralSnowColumnMass + " m");
        
        GUI.Label(new Rect(horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, 4 * element_width, element_height ), "Current FPS: " + GetFPS());

        // right screen ui
        ui_element_no = 0;
        if (GUI.Button(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, 30), "Switch Camera"))
        {
            int cameraCount = cameras.transform.childCount;
            activeCameraNo++;
            activeCameraNo = activeCameraNo % cameraCount;
            for (int i = 0; i < cameraCount; i++)
            {
                GameObject camera = cameras.transform.GetChild(i).gameObject;
                camera.SetActive(false);
                if (i == activeCameraNo) camera.SetActive(true);
            }
        }
        if (GUI.Button(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Paint White"))
        {
            float toggle = GridMaterial.GetFloat("_Paint_White");
            GridMaterial.SetFloat("_Paint_White", toggle == 0.0f ? 1.0f : 0.0f);
        }

        GUI.Label(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Toggle:");
        if (GUI.Button(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Grid"))
        {
            showGrid = !showGrid;
        }

        if (GUI.Button(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Density"))
        {
            float toggle = GridMaterial.GetFloat("_Show_Density");
            GridMaterial.SetFloat("_Show_Density", toggle == 0.0f ? 1.0f : 0.0f);
        }

        if (GUI.Button(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Temperature"))
        {
            float toggle = GridMaterial.GetFloat("_Show_Temperature");
            GridMaterial.SetFloat("_Show_Temperature", toggle == 0.0f ? 1.0f : 0.0f);
        }

        if (GUI.Button(new Rect(screen_width - element_width - horizontal_interval, screep_pos_y_from_top + ui_element_no++ * vertical_interval, element_width, element_height), "Pressure"))
        {
            float toggle = GridMaterial.GetFloat("_Show_Pressure");
            GridMaterial.SetFloat("_Show_Pressure", toggle == 0.0f ? 1.0f : 0.0f);
        }
    }
    private void ToggleColliders()
    {
        if (colliders.activeSelf) colliders.SetActive(false);
        else { colliders.SetActive(true); }
    }
    private void Reset()
    {
        OnDestroy();
        InitDefaultArguments();
        InitializeTweakParameters();
        UpdateTweakParameters();
        CreateTextures();
        GenerateHeightMap();
        InitGrid();
        InitializeColliders();
        UpdateColliders();
    }
}
