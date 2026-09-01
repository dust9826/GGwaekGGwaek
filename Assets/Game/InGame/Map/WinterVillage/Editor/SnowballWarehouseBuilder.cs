#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack.Map.WinterVillage.Editor
{
    /// <summary>
    /// Creates a project-owned snowball warehouse that matches the winter village palette.
    /// Vendor buildings are instantiated only in the comparison render and are never modified.
    /// </summary>
    public static class SnowballWarehouseBuilder
    {
        private const string RootFolder = "Assets/Game/InGame/Map/WinterVillage";
        private const string PrefabFolder = "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow";
        private const string MaterialFolder = RootFolder + "/Materials/SnowballWarehouse";
        private const string GeneratedFolder = RootFolder + "/Generated/SnowballWarehouse";
        private const string PreviewFolder = RootFolder + "/Preview/SnowballWarehouse";
        private const string PrefabPath = PrefabFolder + "/PF_SnowballWarehouse.prefab";
        private const string GableMeshPath = GeneratedFolder + "/MSH_SnowballWarehouse_Gable.asset";
        private const string GiftPrefabPath = "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";

        private const string FairBoothPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Houses/Winter houses/Fairground christmas houses/fairground christmas house.prefab";
        private const string WoodenHousePath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Houses/Winter houses/Wooden winter houses/wooden winter house.prefab";
        private const string FarmBarnPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Houses/Farm houses/farm barn.prefab";
        private const int PreviewLayer = 31;

        private static readonly Color Wood = new Color(0.25f, 0.115f, 0.07f, 1f);
        private static readonly Color WoodLight = new Color(0.44f, 0.22f, 0.105f, 1f);
        private static readonly Color TrimRed = new Color(0.54f, 0.075f, 0.055f, 1f);
        private static readonly Color DoorBlue = new Color(0.055f, 0.25f, 0.31f, 1f);
        private static readonly Color RoofDark = new Color(0.105f, 0.13f, 0.18f, 1f);
        private static readonly Color Snow = new Color(0.79f, 0.89f, 1f, 1f);
        private static readonly Color SnowShade = new Color(0.49f, 0.68f, 0.88f, 1f);
        private static readonly Color Stone = new Color(0.20f, 0.24f, 0.30f, 1f);
        private static readonly Color Metal = new Color(0.16f, 0.20f, 0.24f, 1f);
        private static readonly Color Warm = new Color(1.0f, 0.53f, 0.12f, 1f);

        [MenuItem("PPack/Map/Winter Village/Build Snowball Warehouse")]
        public static void Build()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(GeneratedFolder);

            Material wood = Material("M_SW_Wood", Wood, 0f, 0.18f);
            Material woodLight = Material("M_SW_WoodLight", WoodLight, 0f, 0.22f);
            Material trim = Material("M_SW_TrimRed", TrimRed, 0f, 0.24f);
            Material door = Material("M_SW_DoorBlue", DoorBlue, 0f, 0.26f);
            Material roof = Material("M_SW_RoofDark", RoofDark, 0.05f, 0.30f);
            Material snow = Material("M_SW_Snow", Snow, 0f, 0.58f);
            Material snowShade = Material("M_SW_SnowShade", SnowShade, 0f, 0.48f);
            Material stone = Material("M_SW_Stone", Stone, 0f, 0.17f);
            Material metal = Material("M_SW_Metal", Metal, 0.58f, 0.32f);
            Material warm = Material("M_SW_WarmWindow", Warm, 0f, 0.42f, true, new Color(1.0f, 0.19f, 0.025f) * 4.2f);
            Material[] laneMaterials =
            {
                Material("M_SW_LaneBlue", Gift.ColorForKind(EGiftBoxKind.Blue), 0.05f, 0.30f),
                Material("M_SW_LaneGreen", Gift.ColorForKind(EGiftBoxKind.Green), 0.05f, 0.30f),
                Material("M_SW_LaneYellow", Gift.ColorForKind(EGiftBoxKind.Yellow), 0.05f, 0.30f),
                Material("M_SW_LaneRed", Gift.ColorForKind(EGiftBoxKind.Red), 0.05f, 0.30f)
            };

            Mesh gableMesh = GetOrCreateGableMesh();
            GameObject root = new GameObject("PF_SnowballWarehouse");
            root.transform.position = Vector3.zero;

            Transform structure = Child(root.transform, "Structure");
            Cube(structure, "StoneFoundation", new Vector3(0f, 0.22f, 0f), new Vector3(8.3f, 0.44f, 6.35f), stone, false);
            AddCompoundWallShell(structure, wood, woodLight, stone);

            MeshPart(structure, "FrontGable", gableMesh, new Vector3(0f, 3.86f, -2.96f), Quaternion.identity, new Vector3(1f, 1f, 1f), wood);
            MeshPart(structure, "RearGable", gableMesh, new Vector3(0f, 3.86f, 2.96f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1f, 1f, 1f), wood);

            // Dark roof underlay and soft-blue snow caps use the same silhouette language as the map houses.
            RoofHalf(structure, "RoofLeft", new Vector3(-2.10f, 4.84f, 0f), 27f, roof, 4.75f, 6.65f, 0.30f);
            RoofHalf(structure, "RoofRight", new Vector3(2.10f, 4.84f, 0f), -27f, roof, 4.75f, 6.65f, 0.30f);
            RoofHalf(structure, "SnowCapLeft", new Vector3(-2.13f, 5.03f, -0.02f), 27f, snow, 4.88f, 6.83f, 0.19f);
            RoofHalf(structure, "SnowCapRight", new Vector3(2.13f, 5.03f, -0.02f), -27f, snow, 4.88f, 6.83f, 0.19f);
            Cube(structure, "SnowRidge", new Vector3(0f, 5.98f, -0.02f), new Vector3(0.42f, 0.27f, 6.86f), snow, false, Quaternion.Euler(0f, 0f, 45f));

            // Structural rhythm keeps the silhouette readable from the game's elevated camera.
            AddCornerPosts(structure, woodLight);
            Cube(structure, "FrontTopBeam", new Vector3(0f, 3.70f, -3.02f), new Vector3(7.95f, 0.25f, 0.28f), woodLight, false);
            Cube(structure, "RearTopBeam", new Vector3(0f, 3.70f, 3.02f), new Vector3(7.95f, 0.25f, 0.28f), woodLight, false);
            Cube(structure, "LeftTopBeam", new Vector3(-3.96f, 3.70f, 0f), new Vector3(0.28f, 0.25f, 6.0f), woodLight, false);
            Cube(structure, "RightTopBeam", new Vector3(3.96f, 3.70f, 0f), new Vector3(0.28f, 0.25f, 6.0f), woodLight, false);

            Transform front = Child(root.transform, "FrontLoadingBay");
            Cube(front, "DoorHeader", new Vector3(0f, 3.45f, -3.25f), new Vector3(5.55f, 0.34f, 0.30f), trim, false);

            CreateHingedDoor(front, "LeftDoorPivot", new Vector3(-2.55f, 0.42f, -3.17f), 1f, door, woodLight, metal,
                out Transform leftDoorPivot, out Rigidbody leftDoorBody);
            CreateHingedDoor(front, "RightDoorPivot", new Vector3(2.55f, 0.42f, -3.17f), -1f, door, woodLight, metal,
                out Transform rightDoorPivot, out Rigidbody rightDoorBody);

            // A shallow packed-snow apron makes placement against a village route easy and avoids a doorstep obstacle.
            Cube(front, "LoadingApron", new Vector3(0f, 0.15f, -4.18f), new Vector3(5.75f, 0.20f, 2.05f), snowShade, false);
            Cube(front, "ApronSnow", new Vector3(0f, 0.27f, -4.18f), new Vector3(5.68f, 0.08f, 2.0f), snow, false);
            AddInteriorAccessRamp(front, woodLight, metal, snow);

            AddWarmGableWindow(root.transform, warm, trim, metal);
            Transform[] giftSlots = AddGiftStorageInterior(root.transform, woodLight, metal, warm, laneMaterials);
            AddRearServiceDetails(root.transform, door, woodLight, trim, warm, metal, snow, snowShade);
            AddSnowballRack(root.transform, woodLight, metal, snow, snowShade);
            AddSideCrates(root.transform, woodLight, snow, snowShade);
            AddSnowDetails(root.transform, snow, snowShade);
            AddAnchors(root.transform);

            SnowballWarehouseStorage warehouse = root.AddComponent<SnowballWarehouseStorage>();
            warehouse.Configure(leftDoorPivot, leftDoorBody, rightDoorPivot, rightDoorBody, giftSlots);
            AddWarehouseTrigger(root.transform, "DoorApproachTrigger", new Vector3(0f, 1.65f, -3.30f), new Vector3(6.9f, 3.3f, 4.8f), warehouse, EWarehouseTriggerKind.Approach);
            AddWarehouseTrigger(root.transform, "GiftStorageTrigger", new Vector3(0f, 1.65f, 0.35f), new Vector3(7.1f, 3.2f, 4.8f), warehouse, EWarehouseTriggerKind.GiftStorage);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SnowballWarehouseBuilder] Built " + PrefabPath);
        }

        [MenuItem("PPack/Map/Winter Village/Capture Snowball Warehouse Comparison")]
        public static void BuildAndCaptureComparison()
        {
            Build();
            EnsureFolder(PreviewFolder);

            Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientLight = RenderSettings.ambientLight;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.30f, 0.39f, 0.56f);

                Material snowGround = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_SW_Snow.mat");
                Material stone = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_SW_Stone.mat");
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "SnowComparisonGround";
                ground.transform.position = new Vector3(0f, -0.32f, 0f);
                ground.transform.localScale = new Vector3(40f, 0.55f, 25f);
                ground.GetComponent<Renderer>().sharedMaterial = snowGround;
                SceneManager.MoveGameObjectToScene(ground, previewScene);
                SetLayerRecursively(ground, PreviewLayer);

                PlacePrefab(previewScene, FairBoothPath, new Vector3(-11.5f, 0f, 0f), 1.35f, "Existing_FairBooth");
                PlacePrefab(previewScene, WoodenHousePath, new Vector3(-3.5f, 0f, 0.4f), 1.0f, "Existing_WoodenHouse");
                GameObject customWarehouse = PlacePrefab(previewScene, PrefabPath, new Vector3(7.0f, 0f, 0f), 1.0f, "Selected_CustomWarehouse");

                // Minimal snow banks keep the comparison in the same visual context without hiding footprints.
                for (int i = 0; i < 9; i++)
                {
                    float x = -17f + i * 4.2f;
                    SphereObject(previewScene, "SnowBank_" + i, new Vector3(x, 0.10f, 5.7f + (i % 2) * 0.45f), new Vector3(2.3f, 0.55f, 0.85f), snowGround);
                }

                DirectionalLight(previewScene);
                Camera camera = CameraObject(previewScene);
                camera.backgroundColor = new Color(0.055f, 0.09f, 0.17f);
                camera.clearFlags = CameraClearFlags.SolidColor;

                Capture(camera, new Vector3(22f, 13f, -25f), new Vector3(-1f, 2.6f, 0f), PreviewFolder + "/01_ExistingVsCustom.png", 1600, 900);
                Capture(camera, new Vector3(15f, 9f, -16f), new Vector3(7f, 2.7f, 0f), PreviewFolder + "/02_CustomFrontThreeQuarter.png", 1400, 900);
                Capture(camera, new Vector3(-1f, 7.5f, 12f), new Vector3(7f, 2.6f, 0f), PreviewFolder + "/03_CustomRearThreeQuarter.png", 1400, 900);
                Capture(camera, new Vector3(7f, 17f, -0.5f), new Vector3(7f, 1.2f, 0f), PreviewFolder + "/04_CustomTop.png", 1200, 900);

                if (customWarehouse != null && customWarehouse.TryGetComponent(out SnowballWarehouseStorage warehouse))
                {
                    warehouse.SetDoorOpenImmediate(true);
                    PlacePreviewGifts(previewScene, warehouse);
                    Capture(camera, new Vector3(14.8f, 5.4f, -11.8f), new Vector3(7f, 1.95f, 0.75f), PreviewFolder + "/05_CustomDoorsOpen.png", 1400, 900);
                    Capture(camera, new Vector3(7f, 3.25f, -6.7f), new Vector3(7f, 1.75f, 1.85f), PreviewFolder + "/06_CustomGiftStorage.png", 1400, 900);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(previewScene, true);
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                AssetDatabase.Refresh();
            }

            Debug.Log("[SnowballWarehouseBuilder] Captured comparison and QA views at " + PreviewFolder);
        }

        [MenuItem("PPack/Map/Winter Village/Validate Snowball Warehouse Functionality")]
        public static void ValidateFunctionality()
        {
            Build();
            Scene validationScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                GameObject warehouseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                GameObject giftAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GiftPrefabPath);
                if (warehouseAsset == null || giftAsset == null) throw new InvalidOperationException("Warehouse or gift prefab asset is missing.");

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(warehouseAsset, validationScene);
                SnowballWarehouseStorage storage = instance.GetComponent<SnowballWarehouseStorage>();
                if (storage == null) throw new InvalidOperationException("SnowballWarehouseStorage is missing.");
                if (storage.Capacity != 8) throw new InvalidOperationException("Expected 8 gift storage slots, found " + storage.Capacity + ".");
                if (instance.transform.Find("SnowballEmblem") != null) throw new InvalidOperationException("Obsolete center emblem still exists.");
                if (instance.transform.Find("Structure/MainWalls") != null) throw new InvalidOperationException("Solid wall blocker still exists.");
                if (instance.GetComponentsInChildren<SnowballWarehouseTriggerRelay>(true).Length != 2)
                    throw new InvalidOperationException("Expected approach and gift-storage triggers.");
                Transform storageInterior = instance.transform.Find("GiftStorageInterior");
                if (storageInterior == null) throw new InvalidOperationException("Gift storage interior is missing.");
                int magazineBeds = 0;
                int laneColorStrips = 0;
                for (int childIndex = 0; childIndex < storageInterior.childCount; childIndex++)
                {
                    string childName = storageInterior.GetChild(childIndex).name;
                    if (childName.StartsWith("MagazineBed_")) magazineBeds++;
                    if (childName.StartsWith("LaneColorStrip_")) laneColorStrips++;
                }
                if (magazineBeds != 4) throw new InvalidOperationException("Expected 4 gravity-feed magazine lanes, found " + magazineBeds + ".");
                if (laneColorStrips != 4) throw new InvalidOperationException("Expected 4 color-coded lane strips, found " + laneColorStrips + ".");

                Transform rampTread = instance.transform.Find("FrontLoadingBay/InteriorAccessRamp/WalkableSnowTread");
                if (rampTread == null || rampTread.GetComponent<Collider>() == null)
                    throw new InvalidOperationException("Walkable warehouse-entry ramp collider is missing.");
                float rampAngle = Vector3.Angle(rampTread.up, Vector3.up);
                if (rampAngle > 45f)
                    throw new InvalidOperationException("Warehouse-entry ramp exceeds the penguin's 45-degree walkable limit.");
                Transform interiorFloor = instance.transform.Find("Structure/InteriorFloor");
                if (interiorFloor == null || interiorFloor.GetComponent<Collider>() == null)
                    throw new InvalidOperationException("Walkable warehouse interior floor is missing.");

                Transform leftPivot = instance.transform.Find("FrontLoadingBay/LeftDoorPivot");
                if (leftPivot == null) throw new InvalidOperationException("Left hinged door is missing.");
                Quaternion closedRotation = leftPivot.localRotation;
                storage.SetDoorOpenImmediate(true);
                float openedAngle = Quaternion.Angle(closedRotation, leftPivot.localRotation);
                if (openedAngle < 90f) throw new InvalidOperationException("Door did not reach a usable open angle.");

                Gift[] validationGifts = new Gift[5];
                EGiftBoxKind[] validationKinds =
                {
                    EGiftBoxKind.Blue,
                    EGiftBoxKind.Green,
                    EGiftBoxKind.Yellow,
                    EGiftBoxKind.Red,
                    EGiftBoxKind.Blue
                };
                for (int i = 0; i < validationGifts.Length; i++)
                {
                    GameObject giftObject = (GameObject)PrefabUtility.InstantiatePrefab(giftAsset, validationScene);
                    giftObject.name = "ValidationGift_" + i;
                    Gift gift = giftObject.GetComponent<Gift>();
                    if (gift != null) gift.SetKind(validationKinds[i]);
                    if (gift == null || !storage.TryStoreGift(gift))
                        throw new InvalidOperationException("Gift " + i + " could not be stored.");
                    validationGifts[i] = gift;
                }
                if (storage.StoredCount != validationGifts.Length) throw new InvalidOperationException("Stored gift count did not update.");
                if (validationGifts[4].TryGetComponent(out Rigidbody reserveBody) && !reserveBody.isKinematic)
                    throw new InvalidOperationException("Reserve gift was not held in the upper magazine slot.");

                GameObject actor = new GameObject("ValidationActor");
                SceneManager.MoveGameObjectToScene(actor, validationScene);
                Rigidbody actorBody = actor.AddComponent<Rigidbody>();
                actorBody.isKinematic = true;
                BoxCollider actorCollider = actor.AddComponent<BoxCollider>();
                storage.ReleaseDoorPreviewOverride();
                storage.NotifyTrigger(EWarehouseTriggerKind.Approach, actorCollider, true);
                if (!storage.DoorsRequestedOpen) throw new InvalidOperationException("Approach trigger did not request the doors to open.");
                storage.NotifyTrigger(EWarehouseTriggerKind.Approach, actorCollider, false);
                if (storage.DoorsRequestedOpen) throw new InvalidOperationException("Approach trigger did not release the doors.");

                Debug.Log("[SnowballWarehouseBuilder] Validation passed: Blue/Green/Yellow/Red gravity-feed lanes, 8 retrievable gift slots, entry ramp " +
                    rampAngle.ToString("F1") + " degrees, open angle " + openedAngle.ToString("F1") + " degrees.");
            }
            finally
            {
                EditorSceneManager.CloseScene(validationScene, true);
            }
        }

        private static void AddCornerPosts(Transform parent, Material material)
        {
            float[] xs = { -3.91f, 3.91f };
            float[] zs = { -2.98f, 2.98f };
            foreach (float x in xs)
            foreach (float z in zs)
                Cube(parent, "CornerPost", new Vector3(x, 2.05f, z), new Vector3(0.30f, 3.85f, 0.30f), material, false);
        }

        private static void AddCompoundWallShell(Transform parent, Material wall, Material floor, Material stone)
        {
            // A compound shell leaves a real walkable interior and an unobstructed loading-bay opening.
            Cube(parent, "InteriorFloor", new Vector3(0f, 0.51f, 0f), new Vector3(7.45f, 0.16f, 5.55f), floor, true);
            Cube(parent, "LeftWall", new Vector3(-3.72f, 2.05f, 0f), new Vector3(0.36f, 3.65f, 5.9f), wall, true);
            Cube(parent, "RightWall", new Vector3(3.72f, 2.05f, 0f), new Vector3(0.36f, 3.65f, 5.9f), wall, true);
            Cube(parent, "RearWall", new Vector3(0f, 2.05f, 2.78f), new Vector3(7.45f, 3.65f, 0.36f), wall, true);
            Cube(parent, "FrontLeftPier", new Vector3(-3.25f, 2.05f, -2.78f), new Vector3(0.95f, 3.65f, 0.36f), wall, true);
            Cube(parent, "FrontRightPier", new Vector3(3.25f, 2.05f, -2.78f), new Vector3(0.95f, 3.65f, 0.36f), wall, true);
            Cube(parent, "FrontUpperWall", new Vector3(0f, 3.62f, -2.78f), new Vector3(5.55f, 0.50f, 0.36f), wall, true);
            Cube(parent, "LoadingThreshold", new Vector3(0f, 0.49f, -2.96f), new Vector3(5.55f, 0.12f, 0.48f), stone, true);
        }

        private static void CreateHingedDoor(
            Transform parent,
            string name,
            Vector3 hingePosition,
            float horizontalDirection,
            Material door,
            Material brace,
            Material metal,
            out Transform pivot,
            out Rigidbody body)
        {
            pivot = Child(parent, name);
            pivot.localPosition = hingePosition;

            float panelCenterX = horizontalDirection * 1.25f;
            Cube(pivot, "DoorPanel", new Vector3(panelCenterX, 1.47f, 0f), new Vector3(2.46f, 2.94f, 0.18f), door, true);
            Cube(pivot, "BraceA", new Vector3(panelCenterX, 1.47f, -0.11f), new Vector3(0.17f, 3.05f, 0.12f), brace, false, Quaternion.Euler(0f, 0f, horizontalDirection * 39f));
            Cube(pivot, "BraceB", new Vector3(panelCenterX, 1.47f, -0.12f), new Vector3(0.17f, 3.05f, 0.12f), brace, false, Quaternion.Euler(0f, 0f, horizontalDirection * -39f));
            Sphere(pivot, "Handle", new Vector3(horizontalDirection * 2.25f, 1.45f, -0.17f), 0.09f, metal, false);

            body = pivot.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private static Transform[] AddGiftStorageInterior(Transform parent, Material wood, Material metal,
            Material warm, Material[] laneMaterials)
        {
            Transform interior = Child(parent, "GiftStorageInterior");
            Cube(interior, "BackPanel", new Vector3(0f, 1.62f, 2.48f), new Vector3(6.45f, 2.55f, 0.15f), metal, false);
            Cube(interior, "LowerShelf", new Vector3(0f, 0.90f, 1.98f), new Vector3(6.35f, 0.18f, 1.02f), wood, true);
            Cube(interior, "ShelfTop", new Vector3(0f, 3.15f, 2.18f), new Vector3(6.55f, 0.18f, 0.62f), wood, false);
            Cube(interior, "LeftPost", new Vector3(-3.05f, 1.84f, 2.18f), new Vector3(0.18f, 2.75f, 0.68f), wood, false);
            Cube(interior, "RightPost", new Vector3(3.05f, 1.84f, 2.18f), new Vector3(0.18f, 2.75f, 0.68f), wood, false);
            Cube(interior, "PickupStop", new Vector3(0f, 1.08f, 1.45f), new Vector3(6.35f, 0.22f, 0.14f), metal, false);
            Cube(interior, "ReserveBackStop", new Vector3(0f, 2.24f, 2.43f), new Vector3(6.35f, 0.18f, 0.14f), wood, false);
            for (int i = 1; i <= 3; i++)
            {
                float x = -3.2f + i * 1.6f;
                Cube(interior, "Divider_" + i, new Vector3(x, 1.48f, 2.18f), new Vector3(0.10f, 2.18f, 0.72f), metal, false);
            }

            // Four gravity-feed lanes: reserve gifts wait high at the back, then slide down
            // toward the front pickup lip when the lower gift leaves the rack.
            for (int column = 0; column < 4; column++)
            {
                float x = -2.4f + column * 1.6f;
                Material laneMaterial = laneMaterials[column];
                Cube(interior, "MagazineBed_" + (column + 1).ToString("00"),
                    new Vector3(x, 1.585f, 1.935f), new Vector3(1.28f, 0.08f, 1.40f),
                    wood, false, Quaternion.Euler(-54f, 0f, 0f));
                Cube(interior, "MagazineRailLeft_" + (column + 1).ToString("00"),
                    new Vector3(x - 0.61f, 1.63f, 1.935f), new Vector3(0.08f, 0.13f, 1.46f),
                    metal, false, Quaternion.Euler(-54f, 0f, 0f));
                Cube(interior, "MagazineRailRight_" + (column + 1).ToString("00"),
                    new Vector3(x + 0.61f, 1.63f, 1.935f), new Vector3(0.08f, 0.13f, 1.46f),
                    metal, false, Quaternion.Euler(-54f, 0f, 0f));
                Cube(interior, "LaneColorStrip_" + (column + 1).ToString("00"),
                    new Vector3(x, 1.105f, 1.36f), new Vector3(1.25f, 0.20f, 0.09f),
                    laneMaterial, false);
                Cube(interior, "LaneColorHeader_" + (column + 1).ToString("00"),
                    new Vector3(x, 2.86f, 2.385f), new Vector3(1.18f, 0.20f, 0.08f),
                    laneMaterial, false);
            }

            Transform slotsParent = Child(interior, "GiftSlots");
            Transform[] slots = new Transform[8];
            for (int row = 0; row < 2; row++)
            for (int column = 0; column < 4; column++)
            {
                int index = row * 4 + column;
                Transform slot = Child(slotsParent, "GiftSlot_" + (index + 1).ToString("00"));
                slot.localPosition = new Vector3(
                    -2.4f + column * 1.6f,
                    row == 0 ? 1.02f : 2.15f,
                    row == 0 ? 1.52f : 2.35f);
                slots[index] = slot;
            }

            GameObject lightObject = new GameObject("InteriorWarmLight");
            lightObject.transform.SetParent(interior, false);
            lightObject.transform.localPosition = new Vector3(0f, 3.28f, 0.55f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.52f, 0.20f);
            light.intensity = 3.1f;
            light.range = 7.2f;
            light.shadows = LightShadows.Soft;

            Cube(interior, "CeilingLamp", new Vector3(0f, 3.40f, 0.55f), new Vector3(0.62f, 0.15f, 0.62f), warm, false);
            return slots;
        }

        private static void AddWarehouseTrigger(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 size,
            SnowballWarehouseStorage warehouse,
            EWarehouseTriggerKind kind)
        {
            Transform triggerTransform = Child(parent, name);
            triggerTransform.localPosition = center;
            BoxCollider trigger = triggerTransform.gameObject.AddComponent<BoxCollider>();
            trigger.size = size;
            trigger.isTrigger = true;
            SnowballWarehouseTriggerRelay relay = triggerTransform.gameObject.AddComponent<SnowballWarehouseTriggerRelay>();
            relay.Configure(warehouse, kind);
        }

        private static void AddWarmGableWindow(Transform parent, Material warm, Material trim, Material metal)
        {
            Transform window = Child(parent, "WarmGableWindow");
            Cube(window, "Glow", new Vector3(0f, 4.48f, -3.18f), new Vector3(1.05f, 0.72f, 0.11f), warm, false);
            Cube(window, "FrameTop", new Vector3(0f, 4.90f, -3.25f), new Vector3(1.35f, 0.16f, 0.14f), trim, false);
            Cube(window, "FrameBottom", new Vector3(0f, 4.06f, -3.25f), new Vector3(1.35f, 0.16f, 0.14f), trim, false);
            Cube(window, "FrameLeft", new Vector3(-0.60f, 4.48f, -3.25f), new Vector3(0.16f, 1.0f, 0.14f), trim, false);
            Cube(window, "FrameRight", new Vector3(0.60f, 4.48f, -3.25f), new Vector3(0.16f, 1.0f, 0.14f), trim, false);
            Cube(window, "MullionV", new Vector3(0f, 4.48f, -3.28f), new Vector3(0.10f, 0.82f, 0.12f), metal, false);
            Cube(window, "MullionH", new Vector3(0f, 4.48f, -3.29f), new Vector3(1.12f, 0.10f, 0.12f), metal, false);

            GameObject lightObject = new GameObject("WarmWorkLight");
            lightObject.transform.SetParent(window, false);
            lightObject.transform.position = new Vector3(0f, 4.45f, -3.7f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.47f, 0.14f);
            light.intensity = 4.2f;
            light.range = 7.5f;
            light.shadows = LightShadows.Soft;
        }

        private static void AddSnowballRack(Transform parent, Material wood, Material metal, Material snow, Material shade)
        {
            Transform rack = Child(parent, "ExteriorSnowballRack");
            Cube(rack, "RackBack", new Vector3(4.76f, 1.70f, 0.55f), new Vector3(0.18f, 3.15f, 4.05f), metal, false);
            Cube(rack, "RackRoof", new Vector3(5.08f, 3.42f, 0.55f), new Vector3(2.35f, 0.20f, 4.45f), snow, false, Quaternion.Euler(0f, 0f, -8f));
            Cube(rack, "OuterPostFront", new Vector3(5.94f, 1.72f, -1.34f), new Vector3(0.22f, 3.35f, 0.22f), wood, false);
            Cube(rack, "OuterPostRear", new Vector3(5.94f, 1.72f, 2.44f), new Vector3(0.22f, 3.35f, 0.22f), wood, false);
            Cube(rack, "BottomRail", new Vector3(5.35f, 0.50f, 0.55f), new Vector3(1.42f, 0.22f, 4.05f), wood, false);
            Cube(rack, "MiddleRail", new Vector3(5.38f, 1.72f, 0.55f), new Vector3(1.45f, 0.18f, 4.05f), wood, false);
            Cube(rack, "FrontStop", new Vector3(5.92f, 1.18f, 0.55f), new Vector3(0.15f, 1.35f, 4.05f), wood, false);

            Vector3[] positions =
            {
                new Vector3(5.28f, 0.90f, -0.88f), new Vector3(5.34f, 0.96f, 0.42f), new Vector3(5.25f, 0.88f, 1.73f),
                new Vector3(5.25f, 2.19f, -0.78f), new Vector3(5.28f, 2.22f, 0.57f), new Vector3(5.30f, 2.15f, 1.80f)
            };
            float[] radii = { 0.57f, 0.65f, 0.55f, 0.52f, 0.59f, 0.50f };
            for (int i = 0; i < positions.Length; i++)
                Sphere(rack, "StoredSnowball_" + (i + 1).ToString("00"), positions[i], radii[i], i % 3 == 1 ? shade : snow, false);

            BoxCollider collider = rack.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(5.35f, 1.72f, 0.55f);
            collider.size = new Vector3(1.75f, 3.45f, 4.25f);
        }

        private static void AddRearServiceDetails(Transform parent, Material door, Material wood, Material trim, Material warm, Material metal, Material snow, Material shade)
        {
            Transform rear = Child(parent, "RearServiceBay");
            Cube(rear, "LowerWallRail", new Vector3(0f, 1.15f, 3.08f), new Vector3(7.45f, 0.14f, 0.16f), wood, false);
            Cube(rear, "UpperWallRail", new Vector3(0f, 2.70f, 3.08f), new Vector3(7.45f, 0.14f, 0.16f), wood, false);

            Cube(rear, "ServiceDoor", new Vector3(1.85f, 1.62f, 3.12f), new Vector3(1.55f, 2.58f, 0.18f), door, false);
            Cube(rear, "DoorTop", new Vector3(1.85f, 2.98f, 3.20f), new Vector3(1.88f, 0.18f, 0.18f), trim, false);
            Cube(rear, "DoorLeft", new Vector3(0.99f, 1.63f, 3.20f), new Vector3(0.16f, 2.86f, 0.18f), trim, false);
            Cube(rear, "DoorRight", new Vector3(2.71f, 1.63f, 3.20f), new Vector3(0.16f, 2.86f, 0.18f), trim, false);
            Cube(rear, "DoorBrace", new Vector3(1.85f, 1.62f, 3.24f), new Vector3(0.15f, 2.25f, 0.12f), wood, false, Quaternion.Euler(0f, 0f, -36f));
            Sphere(rear, "DoorHandle", new Vector3(2.38f, 1.62f, 3.29f), 0.09f, metal, false);

            Cube(rear, "WindowGlow", new Vector3(-1.55f, 2.10f, 3.13f), new Vector3(1.42f, 0.92f, 0.12f), warm, false);
            Cube(rear, "WindowTop", new Vector3(-1.55f, 2.63f, 3.20f), new Vector3(1.70f, 0.14f, 0.15f), trim, false);
            Cube(rear, "WindowBottom", new Vector3(-1.55f, 1.57f, 3.20f), new Vector3(1.70f, 0.14f, 0.15f), trim, false);
            Cube(rear, "WindowLeft", new Vector3(-2.32f, 2.10f, 3.20f), new Vector3(0.14f, 1.18f, 0.15f), trim, false);
            Cube(rear, "WindowRight", new Vector3(-0.78f, 2.10f, 3.20f), new Vector3(0.14f, 1.18f, 0.15f), trim, false);
            Cube(rear, "WindowMullionV", new Vector3(-1.55f, 2.10f, 3.25f), new Vector3(0.09f, 0.98f, 0.10f), metal, false);
            Cube(rear, "WindowMullionH", new Vector3(-1.55f, 2.10f, 3.25f), new Vector3(1.50f, 0.09f, 0.10f), metal, false);

            Cube(rear, "ServiceApron", new Vector3(1.85f, 0.13f, 3.75f), new Vector3(2.35f, 0.15f, 1.15f), shade, false);
            Cube(rear, "ServiceApronSnow", new Vector3(1.85f, 0.22f, 3.75f), new Vector3(2.28f, 0.06f, 1.08f), snow, false);
        }

        private static void AddSideCrates(Transform parent, Material wood, Material snow, Material shade)
        {
            Transform props = Child(parent, "LoadingProps");
            Crate(props, "PackedSnowCrate_A", new Vector3(-4.65f, 0.65f, -1.75f), new Vector3(1.45f, 1.15f, 1.55f), wood);
            Crate(props, "PackedSnowCrate_B", new Vector3(-4.75f, 0.53f, 0.05f), new Vector3(1.20f, 0.92f, 1.30f), wood);
            Sphere(props, "LooseSnowball_A", new Vector3(-4.65f, 1.58f, -1.78f), 0.47f, snow, false);
            Sphere(props, "LooseSnowball_B", new Vector3(-4.34f, 1.36f, -1.27f), 0.37f, shade, false);
            Sphere(props, "LooseSnowball_C", new Vector3(-5.00f, 1.42f, -1.25f), 0.34f, snow, false);
        }

        private static void AddSnowDetails(Transform parent, Material snow, Material shade)
        {
            Transform details = Child(parent, "SnowAccumulation");
            Cube(details, "FrontLintelSnow", new Vector3(0f, 3.71f, -3.38f), new Vector3(5.7f, 0.14f, 0.42f), snow, false);
            Sphere(details, "LeftDrift", new Vector3(-3.70f, 0.32f, -3.00f), 0.52f, snow, false, new Vector3(1.4f, 0.48f, 1.1f));
            Sphere(details, "RightDrift", new Vector3(3.62f, 0.30f, -3.04f), 0.48f, shade, false, new Vector3(1.25f, 0.42f, 1.0f));
            Sphere(details, "RackDrift", new Vector3(5.65f, 0.27f, 2.48f), 0.48f, snow, false, new Vector3(1.6f, 0.42f, 1.1f));
        }

        private static void AddInteriorAccessRamp(Transform parent, Material wood, Material metal,
            Material snow)
        {
            Transform access = Child(parent, "InteriorAccessRamp");
            const float angle = 6.8f;
            Quaternion slope = Quaternion.Euler(-angle, 0f, 0f);

            // A broad, shallow threshold ramp links the outdoor apron directly to the interior
            // floor. The collider belongs to the visible tread, so the rendered path and the
            // physical path cannot drift apart.
            Cube(access, "RampUnderlay", new Vector3(0f, 0.29f, -3.65f),
                new Vector3(5.32f, 0.22f, 2.68f), wood, false, slope);
            Cube(access, "WalkableSnowTread", new Vector3(0f, 0.38f, -3.65f),
                new Vector3(5.25f, 0.14f, 2.60f), snow, true, slope);

            // These are visual guides only. Omitting colliders prevents the penguin and pushed
            // gifts from catching on a narrow edge at the doorway.
            Cube(access, "LeftEdgeGuide", new Vector3(-2.67f, 0.49f, -3.65f),
                new Vector3(0.14f, 0.13f, 2.68f), metal, false, slope);
            Cube(access, "RightEdgeGuide", new Vector3(2.67f, 0.49f, -3.65f),
                new Vector3(0.14f, 0.13f, 2.68f), metal, false, slope);
        }

        private static void AddAnchors(Transform parent)
        {
            Transform anchors = Child(parent, "GameplayAnchors");
            Empty(anchors, "LoadingPoint", new Vector3(0f, 0.35f, -4.6f), Quaternion.identity);
            Empty(anchors, "VehicleApproach", new Vector3(0f, 0.35f, -8.0f), Quaternion.identity);
            Transform spawns = Child(anchors, "SnowballSpawnPoints");
            Empty(spawns, "Spawn_A", new Vector3(5.25f, 1.0f, -0.85f), Quaternion.identity);
            Empty(spawns, "Spawn_B", new Vector3(5.25f, 1.0f, 0.55f), Quaternion.identity);
            Empty(spawns, "Spawn_C", new Vector3(5.25f, 2.2f, 0.55f), Quaternion.identity);
        }

        private static void DoorBrace(Transform parent, string name, Vector3 position, float angle, Material material)
        {
            Cube(parent, name, position, new Vector3(0.17f, 3.10f, 0.12f), material, false, Quaternion.Euler(0f, 0f, angle));
        }

        private static void Crate(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            Transform crate = Child(parent, name);
            Cube(crate, "Body", position, size, material, false);
            float y = position.y;
            float xHalf = size.x * 0.44f;
            float zHalf = size.z * 0.44f;
            Cube(crate, "BandFront", new Vector3(position.x, y, position.z - size.z * 0.51f), new Vector3(size.x * 1.05f, 0.18f, 0.12f), material, false);
            Cube(crate, "BandBack", new Vector3(position.x, y, position.z + size.z * 0.51f), new Vector3(size.x * 1.05f, 0.18f, 0.12f), material, false);
            Cube(crate, "PostA", new Vector3(position.x - xHalf, y, position.z - zHalf), new Vector3(0.15f, size.y * 1.05f, 0.15f), material, false);
            Cube(crate, "PostB", new Vector3(position.x + xHalf, y, position.z - zHalf), new Vector3(0.15f, size.y * 1.05f, 0.15f), material, false);
        }

        private static void RoofHalf(Transform parent, string name, Vector3 position, float zAngle,
            Material material, float width, float depth, float thickness, bool keepCollider = false)
        {
            Cube(parent, name, position, new Vector3(width, thickness, depth), material, keepCollider,
                Quaternion.Euler(0f, 0f, zAngle));
        }

        private static Mesh GetOrCreateGableMesh()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(GableMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "MSH_SnowballWarehouse_Gable" };
                AssetDatabase.CreateAsset(mesh, GableMeshPath);
            }

            float halfWidth = 3.9f;
            float height = 2.1f;
            float halfDepth = 0.13f;
            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfDepth), new Vector3(halfWidth, 0f, -halfDepth), new Vector3(0f, height, -halfDepth),
                new Vector3(-halfWidth, 0f, halfDepth), new Vector3(halfWidth, 0f, halfDepth), new Vector3(0f, height, halfDepth)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 3, 4, 5,
                0, 3, 5, 0, 5, 2,
                1, 2, 5, 1, 5, 4,
                0, 1, 4, 0, 4, 3
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material Material(string name, Color color, float metallic, float smoothness, bool emission = false, Color emissionColor = default)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emissionColor);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Empty(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localRotation = rotation;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool keepCollider, Quaternion rotation = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 position, float radius, Material material, bool keepCollider, Vector3 scaleMultiplier = default)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            Vector3 multiplier = scaleMultiplier == default ? Vector3.one : scaleMultiplier;
            go.transform.localScale = multiplier * (radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool keepCollider, Quaternion rotation)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void MeshPart(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static GameObject PlacePrefab(Scene scene, string path, Vector3 position, float scale, string name)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return null;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            SetLayerRecursively(go, PreviewLayer);
            return go;
        }

        private static void PlacePreviewGifts(Scene scene, SnowballWarehouseStorage warehouse)
        {
            GameObject giftAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GiftPrefabPath);
            if (giftAsset == null || warehouse.StorageSlots == null) return;

            int count = Mathf.Min(6, warehouse.StorageSlots.Count);
            for (int i = 0; i < count; i++)
            {
                Transform slot = warehouse.StorageSlots[i];
                if (slot == null) continue;
                GameObject gift = (GameObject)PrefabUtility.InstantiatePrefab(giftAsset, scene);
                gift.name = "PreviewStoredGift_" + (i + 1).ToString("00");
                gift.transform.SetPositionAndRotation(slot.position, slot.rotation);
                if (gift.TryGetComponent(out GiftAppearance appearance)) appearance.Randomize(9001 + i * 73);
                Renderer[] renderers = gift.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
                        bounds.Encapsulate(renderers[rendererIndex].bounds);
                    gift.transform.position += Vector3.up * (slot.position.y - bounds.min.y + 0.015f);
                }
                if (gift.TryGetComponent(out Rigidbody body)) body.isKinematic = true;
                SetLayerRecursively(gift, PreviewLayer);
            }
        }

        private static void SphereObject(Scene scene, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            SetLayerRecursively(go, PreviewLayer);
            SceneManager.MoveGameObjectToScene(go, scene);
        }

        private static void DirectionalLight(Scene scene)
        {
            GameObject go = new GameObject("ComparisonKeyLight");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.Euler(46f, -32f, 0f);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.77f, 0.86f, 1f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
            light.cullingMask = 1 << PreviewLayer;

            GameObject warmFill = new GameObject("ComparisonWarmFill");
            SceneManager.MoveGameObjectToScene(warmFill, scene);
            warmFill.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            Light fill = warmFill.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(1f, 0.55f, 0.31f);
            fill.intensity = 0.32f;
            fill.shadows = LightShadows.None;
            fill.cullingMask = 1 << PreviewLayer;
        }

        private static Camera CameraObject(Scene scene)
        {
            GameObject go = new GameObject("ComparisonCamera");
            SceneManager.MoveGameObjectToScene(go, scene);
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;
            camera.allowHDR = true;
            camera.cullingMask = 1 << PreviewLayer;
            return camera;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static void Capture(Camera camera, Vector3 position, Vector3 target, string path, int width, int height)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
            RenderTexture texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                RenderTexture.active = texture;
                camera.Render();
                Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(texture);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
