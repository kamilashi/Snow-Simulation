using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Manager : MonoBehaviour
{
    public ComputeShader shader;

    public int texResolution;

    public bool showGrid = true;
    public bool showSnowParticles = true;
    public bool toggle = false;
    public Vector3 gridCenter;
    public float cellSize;
    private float planeSideSize;

    [Range(0.0f, 1.0f)]
    public float timeScale = 0.5f;
    [Range(0.0f, 1.0f)]
    public float simulationSpeed = 0.5f;
    [Range(0.0f, 10.0f)]
    public float snowHeightScale = 5.0f;
    private float time = 0.0f;

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
        public float hardness;
        public float temperature;
        public float grainSize;
        public float mass;
        public int index;
        public int isOccupied; //TO-DO - enum here

        public Cell(int gridX, int gridY, int gridZ, int cubeIndex)
        {
            gridIndex = new Vector3(gridX, gridY, gridZ);
            WSposition =  Vector3.zero;
            force = Vector3.zero;
            density = 0.2f;/// 1000000.0f;
            hardness = 50.0f;
            temperature = -3.0f;
            mass = 0.0f;
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

    int SIZE_CELL = 5 * sizeof(int) + 11 * sizeof(float);
    int SIZE_PARTICLE = 7 * sizeof(float);
    Cell[] cellGridArray;
    Particle[] particleArray;

    ComputeBuffer cellGridBuffer;
    ComputeBuffer particleBuffer;

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

    uint gridThreadGroupSizeX;
    uint gridTthreadGroupSizeY;
    uint gridThreadGroupSizeZ;

    // Start is called before the first frame update
    void Start()
    {
        CreateTextures();
        GenerateHeightMap();
        InitDefaultArguments();
        InitGrid();
        //InitSnowParticles();
    }

    Renderer rend;
    RenderTexture groundHeightMapTexture;
    //RenderTexture snowInputHeightMapTexture;
    RenderTexture snowHeightMapTexture;
    RenderTexture debugText;
    private void CreateTextures()
    {
            groundHeightMapTexture = new RenderTexture(texResolution, texResolution, 0);
            groundHeightMapTexture.enableRandomWrite = true;
            groundHeightMapTexture.Create();

            //snowInputHeightMapTexture = new RenderTexture(texResolution, texResolution, 0);
            //snowInputHeightMapTexture.enableRandomWrite = true;
            //snowInputHeightMapTexture.Create();

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
    int kernelHandleHeight;
    int kernelInitHeight;
    private void GenerateHeightMap()
    {
        kernelHandleHeight = shader.FindKernel("GenerateHeight");

        kernelInitHeight = shader.FindKernel("InitSnowHeight");

        shader.SetInt("texResolution", texResolution);
        shader.SetTexture(kernelHandleHeight, "GroundHeightMap", groundHeightMapTexture);
        //shader.SetTexture(kernelHandleHeight, "SnowInputHeightMap", snowInputHeightMapTexture);
        shader.SetTexture(kernelHandleHeight, "Debug", debugText);


        shader.SetTexture(kernelInitHeight, "SnowHeightMap", snowHeightMapTexture);

        shader.SetFloat("snowHeightFactor", snowHeightScale); //important for reconstruction
        groundMaterial.SetTexture("_GroundHeightMap", groundHeightMapTexture);
        snowMaterial.SetTexture("_SnowHeightMap", snowHeightMapTexture);
        snowMaterial.SetTexture("_GroundHeightMap", groundHeightMapTexture);
        snowMaterial.SetFloat("_SnowFactorMax", snowHeightScale);

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


        shader.GetKernelThreadGroupSizes(kernelHandleHeight, out heightThreadGroupSizeX, out heightThreadGroupSizeY, out _);
        shader.Dispatch(kernelHandleHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);
       
        shader.Dispatch(kernelInitHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);

    }

    private void InitDefaultArguments()
    {

        texResolution = 1024;
        gridWidth = 50;
        gridHeight = 50;
        gridDepth = 50;
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
                    Cell cell = new Cell(k,j,i, index);
                    cellGridArray[index] = cell;
                    index++;
                }
            }
        }

        cellGridBuffer = new ComputeBuffer(cellCount, SIZE_CELL);
        cellGridBuffer.SetData(cellGridArray);



        kernePopulateGrid = shader.FindKernel("PopulateGrid");
        shader.SetBuffer(kernePopulateGrid, "cellGridBuffer", cellGridBuffer);

        int[] gridDimensions = new int[] { gridWidth, gridHeight, gridDepth };
        Debug.Log(gridDimensions.ToString());
        shader.SetInts( "gridDimensions", gridDimensions); //in cell numbers! 
        shader.SetFloat("cellSize", cellSize);
        float[] gridC = new float[] { gridCenter.x, gridCenter.y, gridCenter.z };
       // Debug.Log("gridC " + gridC[1]);
        shader.SetFloats("gridCenter", gridC);
        shader.SetInt("cellBufferLength", cellCount);
        shader.SetTexture(kernePopulateGrid, "GroundHeightMap", groundHeightMapTexture);
        shader.SetTexture(kernePopulateGrid, "SnowHeightMap", snowHeightMapTexture);
        shader.SetTexture(kernePopulateGrid, "Debug", debugText);

        
        shader.GetKernelThreadGroupSizes(kernePopulateGrid, out gridThreadGroupSizeX, out gridTthreadGroupSizeY, out gridThreadGroupSizeZ);
        shader.Dispatch(kernePopulateGrid, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX), 
                                           Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY), 
                                           Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

        GridMaterial.SetBuffer("cellGridBuffer", cellGridBuffer);
        GridMaterial.SetFloat("_CellSize", cellSize);

        gridArgs = new uint[] { cubeMesh.GetIndexCount(0), (uint)cellCount, 0, 0, 0 };
        gridArgsBuffer = new ComputeBuffer(1, gridArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        gridArgsBuffer.SetData(gridArgs);
        cellBounds = new Bounds(Vector3.zero, new Vector3(30, 30, 30)); //place holder

    }
    Bounds particleBounds;
    private int kernelInitSnow;
    private int kernelSetGridVelocity;
    private int kernelPropagateForce;
    private int kernelCollisionDetection;
    private int kernelUpdateParticlePosition;
    private int kernelUpdateParticleVelocity;
    private int kernelDissipateForces;
    Vector3Int particlesPerAxis;
    uint snowThreadGroupSizeX;
    uint snowTthreadGroupSizeY;
    uint snowThreadGroupSizeZ;
    int snowRows;
    private void InitSnowParticles()
    {
        snowRows = 5;
        particlesPerAxis.x = Mathf.CeilToInt(planeSideSize / (particleSize * 2.0f));
        particlesPerAxis.z = Mathf.CeilToInt(planeSideSize / (particleSize * 2.0f));
        particlesPerAxis.y = Mathf.CeilToInt(snowHeightScale / (particleSize * 4.0f));

        particleCount = particlesPerAxis.x * particlesPerAxis.y * particlesPerAxis.z;
        Debug.Log("particle count " + particleCount);



        particleArray = new Particle[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
          //  Vector3 wsPos = new Vector3(0.0f,0.0f,0.0f);
            //wsPos = transform.TransformPoint(wsPos);
            Particle particle = new Particle(particleSize);
            //particle.velocity.y = 0.0f;
            //Debug.Log("particle.pos.y = " + particle.position.y);
            particleArray[i] = particle;
        }

        particleBuffer = new ComputeBuffer(particleCount, SIZE_PARTICLE);
        particleBuffer.SetData(particleArray);

        kernelInitSnow = shader.FindKernel("InitSnowParticles");
        shader.SetBuffer(kernelInitSnow, "particleBuffer", particleBuffer);

        kernelSetGridVelocity = shader.FindKernel("SetGridVelocity");
        shader.SetBuffer(kernelSetGridVelocity, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelSetGridVelocity, "SnowHeightMap", snowHeightMapTexture);
        shader.SetTexture(kernelSetGridVelocity, "GroundHeightMap", groundHeightMapTexture);

        kernelPropagateForce = shader.FindKernel("PropagateForce");
        shader.SetBuffer(kernelPropagateForce, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelPropagateForce, "SnowHeightMap", snowHeightMapTexture);
        shader.SetTexture(kernelPropagateForce, "GroundHeightMap", groundHeightMapTexture);

        kernelCollisionDetection = shader.FindKernel("CollisionDetection");
        shader.SetBuffer(kernelCollisionDetection, "particleBuffer", particleBuffer);
        shader.SetBuffer(kernelCollisionDetection, "cellGridBuffer", cellGridBuffer);
        shader.SetTexture(kernelCollisionDetection, "GroundHeightMap", groundHeightMapTexture);
        shader.SetTexture(kernelCollisionDetection, "SnowHeightMap", snowHeightMapTexture);

        kernelUpdateParticlePosition = shader.FindKernel("UpdateParticlePosition");
        shader.SetBuffer(kernelUpdateParticlePosition, "particleBuffer", particleBuffer);
        shader.SetBuffer(kernelUpdateParticlePosition, "cellGridBuffer", cellGridBuffer);

        kernelUpdateParticleVelocity = shader.FindKernel("UpdateParticleVelocity");
        shader.SetBuffer(kernelUpdateParticleVelocity, "particleBuffer", particleBuffer);
        shader.SetBuffer(kernelUpdateParticleVelocity, "cellGridBuffer", cellGridBuffer);

        kernelDissipateForces = shader.FindKernel("DissipateForces");
        shader.SetBuffer(kernelDissipateForces, "cellGridBuffer", cellGridBuffer);

        int[] parPerAxis = new int[] { particlesPerAxis.x, particlesPerAxis.y, particlesPerAxis.z };
        shader.SetInts("particlesPerAxis", parPerAxis); //in cell numbers! 
        shader.SetTexture(kernelInitSnow, "GroundHeightMap", groundHeightMapTexture);
        shader.SetTexture(kernelInitSnow, "SnowHeightMap", snowHeightMapTexture);

        //no particles for now

       shader.GetKernelThreadGroupSizes(kernelInitSnow, out snowThreadGroupSizeX, out snowTthreadGroupSizeY, out snowThreadGroupSizeZ);
       shader.Dispatch(kernelInitSnow, Mathf.CeilToInt((float)particlesPerAxis.x / (float)snowThreadGroupSizeX),
                                          Mathf.CeilToInt((float)particlesPerAxis.y / (float)snowTthreadGroupSizeY),
                                          Mathf.CeilToInt((float)particlesPerAxis.z / (float)snowThreadGroupSizeZ));
        shader.Dispatch(kernelSetGridVelocity, 1, 1, 1);


        particleMaterial.SetBuffer("particleBuffer", particleBuffer);

        particleArgs = new uint[] { particleMesh.GetIndexCount(0), (uint)particleCount, 0, 0, 0 };
        particleArgsBuffer = new ComputeBuffer(1, particleArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        particleArgsBuffer.SetData(particleArgs);
        particleBounds = new Bounds(Vector3.zero, new Vector3(30, 30, 30)); //place holder

    }
        // Update is called once per frame
        void Update()
    {

        shader.SetFloat("snowHeightFactor", snowHeightScale); //important for reconstruction
        snowMaterial.SetFloat("_SnowFactorMax", snowHeightScale);
        shader.SetFloat("deltaTime", Time.deltaTime);
        shader.SetFloat("time", time += Time.deltaTime);
        shader.SetFloat("timeScale", timeScale);
        shader.SetFloat("simulationSpeed", simulationSpeed);

        //shader.GetKernelThreadGroupSizes(kernelHandleHeight, out heightThreadGroupSizeX, out heightThreadGroupSizeY, out _);
        shader.Dispatch(kernelHandleHeight, Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeX), Mathf.CeilToInt((float)texResolution / (float)heightThreadGroupSizeY), 1);
        // clears grid
        shader.Dispatch(kernePopulateGrid, Mathf.CeilToInt((float)gridWidth / (float)gridThreadGroupSizeX),
                                              Mathf.CeilToInt((float)gridHeight / (float)gridTthreadGroupSizeY),
                                              Mathf.CeilToInt((float)gridDepth / (float)gridThreadGroupSizeZ));

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

        //particleBuffer.Release();

        //if (particleArgsBuffer != null)
        //{

        //    //Debug.Log("args buffer released " + argsBuffer);
        //    particleArgsBuffer.Release();
        //}
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
