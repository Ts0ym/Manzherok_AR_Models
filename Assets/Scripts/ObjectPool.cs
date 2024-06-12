using System.Collections.Generic;
using UnityEngine;

public class ObjectPool
{
    private readonly Queue<GameObject> objects = new Queue<GameObject>();
    private readonly GameObject prefab;

    public ObjectPool(GameObject prefab)
    {
        this.prefab = prefab;
    }

    public GameObject Get()
    {
        if (objects.Count == 0)
        {
            AddObjects(1);
        }
        return objects.Dequeue();
    }

    public void ReturnToPool(GameObject objectToReturn)
    {
        objectToReturn.SetActive(false);
        objects.Enqueue(objectToReturn);
    }

    private void AddObjects(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var newObject = GameObject.Instantiate(prefab);
            newObject.SetActive(false);
            objects.Enqueue(newObject);
        }
    }
}
