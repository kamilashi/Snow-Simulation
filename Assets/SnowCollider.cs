using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SnowCollider : MonoBehaviour
{
    public float mass = 10;
    public float cellSize = 0.2f;
    public float heightPosition = 1;
    public int cellCount;

    const float G = 9.81f;

    public Manager.CollisionData[] collisionsArray;

    [SerializeField] private float collisionArea;
    [SerializeField] private Vector3 dimensions;
    private Bounds bounds;
    [SerializeField] private Vector3 pressure; //Unit pressure
    private int x_cells;
    private int z_cells;

    private void Awake()
    {
        bounds = GetComponent<MeshRenderer>().bounds;
        dimensions = new Vector3(bounds.size.x, bounds.size.y, bounds.size.z);
        collisionArea = bounds.size.x * bounds.size.z;
        float force_mg = -1.0f * (mass * G);
        pressure.y = force_mg / collisionArea;
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

        Vector3 newPosition = new Vector3(transform.position.x, heightPosition + bounds.extents.y - cellSize, transform.position.z);
        transform.SetPositionAndRotation(newPosition, Quaternion.identity);

        bounds = GetComponent<MeshRenderer>().bounds;
        dimensions = new Vector3(bounds.size.x, bounds.size.y, bounds.size.z);
        collisionArea = bounds.size.x * bounds.size.z;
        float force_mg = -1.0f * (mass * G);
        pressure.y = force_mg / collisionArea;

        CalculateCollision();
    }
    public void SetHeight(float snowHeight)
    {
        heightPosition = snowHeight;
    }

    public void CalculateCollision()
    {
        Vector3 center = transform.position;
        int data_index = 0;
        Vector3 minPos = center - bounds.extents + new Vector3(- cellSize*0.5f, 0,  cellSize * 0.5f);

            for (int i = 0; i < x_cells; i++)
            {
                for (int j = 0; j < z_cells; j++)
                {
                    collisionsArray[data_index].position = minPos + new Vector3(i, 0, j) * cellSize;
                    collisionsArray[data_index].pressure = pressure;
                    data_index++;
                }
            }
    }
    public void Init()
    {
        x_cells = Mathf.CeilToInt(bounds.size.x / cellSize);
        z_cells = Mathf.CeilToInt(bounds.size.z / cellSize);
        cellCount = (x_cells + 0) * (z_cells + 0);
        collisionsArray = new Manager.CollisionData[cellCount];
    }
    public Manager.CollisionData[] getCollisionData()
    {
        return collisionsArray;
    }
}
