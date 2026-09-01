using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    public static class AmbientTrafficSceneRigBuilder
    {
        private const float RoadSurfaceY = 0.31f;

        private static readonly string[] VehiclePrefabPaths =
        {
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Sedans cars/sedan car blue.prefab",
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Universal cars/universal car red.prefab",
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Old minivan cars/old minivan car green.prefab",
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Cars tourist SUVs/car tourist SUV yellow.prefab",
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Cars SUVs large/car SUV large brown.prefab",
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Cars SUVs pickups lights/car SUV pickup lights blue.prefab"
        };

        public static AmbientTrafficSpawner Build(Transform parent)
        {
            var visuals = new List<GameObject>(VehiclePrefabPaths.Length);
            foreach (string path in VehiclePrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException($"주변 차량 프리팹이 없다: {path}");
                visuals.Add(prefab);
            }

            var trafficObject = new GameObject("AmbientTraffic");
            trafficObject.transform.SetParent(parent);
            AmbientTrafficSpawner spawner = trafficObject.AddComponent<AmbientTrafficSpawner>();
            spawner.Configure(visuals, vehicleCount: 6, seed: 20260825,
                roadSurfaceY: RoadSurfaceY);
            return spawner;
        }
    }
}
