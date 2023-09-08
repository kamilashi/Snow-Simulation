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
    [SerializeField] private Vector3 pressure; 
    private int cellCountAlongX;
    private int cellCountAlongZ;

    void UpdateAreaAndPressure() {
        bounds = GetComponent<MeshRenderer>().bounds;
        dimensions = new Vector3(bounds.size.x , bounds.size.y , bounds.size.z );
        collisionArea = dimensions.x * dimensions.z;
        float force_mg = -1.0f * (mass * G);
        pressure.y = force_mg / collisionArea;
    }

    private void Awake() // called before the first frame Updae, but even if the object is disabled in the scene
    {
        UpdateAreaAndPressure();
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 newPosition = new Vector3(transform.position.x, heightPosition + dimensions.y*0.5f - cellSize, transform.position.z);
        transform.SetPositionAndRotation(newPosition, Quaternion.identity);
        UpdateAreaAndPressure();
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
        Vector3 minPos = center - dimensions*0.5f + new Vector3(- cellSize*0.5f, 0,  cellSize * 0.5f);

            for (int i = 0; i < cellCountAlongX; i++)
            {
                for (int j = 0; j < cellCountAlongZ; j++)
                {
                    collisionsArray[data_index].position = minPos + new Vector3(i, 0, j) * cellSize;
                    collisionsArray[data_index].pressure = pressure;
                    data_index++;
                }
            }
    }
    public void Initialize()
    {
        cellCountAlongX = Mathf.CeilToInt(dimensions.x / cellSize);
        cellCountAlongZ = Mathf.CeilToInt(dimensions.z / cellSize);
        cellCount = (cellCountAlongX + 0) * (cellCountAlongZ + 0);
        collisionsArray = new Manager.CollisionData[cellCount];
    }
    public Manager.CollisionData[] getCollisionData()
    {
        return collisionsArray;
    }
}
