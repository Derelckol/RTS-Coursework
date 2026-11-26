using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace Rts.Editor
{
    public static class StylePreviewBuilder
    {
        private const string Folder = "Assets/RTS/StylePreview";
        private const string ScenePath = "Assets/Scenes/RtsStylePreview.unity";

        [MenuItem("RTS/Create style preview")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.57f,.62f,.68f);
            var root = new GameObject("Medieval style samples");
            var grass = Material("Meadow", 0x647852);
            var dirt = Material("Packed earth", 0xA99970);
            var wood = Material("Oak", 0x61432E);
            var pale = Material("Lime plaster", 0xE9D3A3);
            var iron = Material("Iron", 0x414C52);
            var stone = Material("Slate", 0x737979);
            var slate = Material("Dark slate", 0x48545B);
            var mortar = Material("Wall mortar", 0x62564B);
            var limestone = Material("Warm limestone", 0x9C8870);
            var sandstone = Material("Pale sandstone", 0xB19A7F);
            var horse = Material("Chestnut horse", 0x85583D);
            var gold = Material("Gold veins", 0xE5AD37);
            var blue = Material("Banner blue", 0x237DB5);
            var red = Material("Banner red", 0xB43F37);
            var skin = Material("Skin", 0xDB9F69);
            var ringMaterial = Material("Selection gold", 0xFFF080, true);
            Block(root.transform,"Meadow",new Vector3(0,-.09f,0),new Vector3(80,.18f,60),grass);
            Block(root.transform,"Packed earth lane",new Vector3(0,-.025f,0),new Vector3(16,.06f,2.6f),dirt);

            const string modelFolder = Folder + "/Resources";
            if (!AssetDatabase.IsValidFolder(modelFolder)) AssetDatabase.CreateFolder(Folder, "Resources");
            if (!AssetDatabase.IsValidFolder(modelFolder + "/MatchModels"))
                AssetDatabase.CreateFolder(modelFolder, "MatchModels");
            var actors = new List<CombatTarget>();
            GameObject selection = null;
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -3.65f : 3.65f;
                var team = side == 0 ? blue : red;
                var label = side == 0 ? "Blue" : "Red";
                var worker = Anchor(root.transform,label+" worker",new Vector3(x,.85f,-1.7f));
                var capsule = worker.AddComponent<CapsuleCollider>();
                capsule.radius = .4f; capsule.height = 1.7f;
                worker.transform.rotation = Quaternion.Euler(0,side == 0 ? -20 : 20,0);
                var unit = worker.AddComponent<PrototypeUnit>();
                var unitSettings = new SerializedObject(unit);
                unitSettings.FindProperty("playerOwned").boolValue = side == 0;
                var ring = Ring(worker.transform,ringMaterial);
                ring.SetActive(false);
                unitSettings.FindProperty("selectionMarker").objectReferenceValue = ring;
                unitSettings.ApplyModifiedPropertiesWithoutUndo();
                if (side == 0) selection = ring;
                actors.Add(worker.AddComponent<CombatTarget>());
                Block(worker.transform,"Tunic",new Vector3(0,.05f,0),new Vector3(.40f,.55f,.31f),team);
                Block(worker.transform,"Belt",new Vector3(0,-.19f,0),new Vector3(.43f,.07f,.34f),wood);
                Block(worker.transform,"Face",new Vector3(0,.46f,-.015f),new Vector3(.29f,.30f,.28f),skin);
                Block(worker.transform,"Cap brim",new Vector3(0,.57f,-.05f),new Vector3(.38f,.07f,.40f),pale);
                Block(worker.transform,"Cap crown",new Vector3(0,.66f,0),new Vector3(.31f,.13f,.29f),pale);
                foreach (float sign in new[]{-1f,1f})
                {
                    Block(worker.transform,"Boot",new Vector3(sign*.115f,-.67f,-.03f),new Vector3(.16f,.36f,.24f),iron);
                    Block(worker.transform,"Sleeve",new Vector3(sign*.26f,-.015f,0),new Vector3(.13f,.39f,.16f),team,sign*12);
                }
                Block(worker.transform,"Supply satchel",new Vector3(-.23f,-.17f,.16f),new Vector3(.24f,.30f,.25f),pale);
                Block(worker.transform,"Satchel flap",new Vector3(-.23f,-.02f,.17f),new Vector3(.25f,.085f,.26f),wood);
                Block(worker.transform,"Pick handle",new Vector3(.29f,.02f,-.18f),new Vector3(.075f,1.02f,.075f),wood,-12);
                Block(worker.transform,"Pick head",new Vector3(.33f,.47f,-.18f),new Vector3(.34f,.12f,.13f),iron,-12);

                Block(worker.transform,"Mallet handle",new Vector3(.29f,.01f,-.18f),new Vector3(.075f,.70f,.075f),wood);
                Block(worker.transform,"Mallet head",new Vector3(.31f,.35f,-.18f),new Vector3(.30f,.17f,.18f),iron);
                Export(worker, label + "Worker");
                worker.transform.Find("Mallet handle").GetComponent<Renderer>().forceRenderingOff = true;
                worker.transform.Find("Mallet head").GetComponent<Renderer>().forceRenderingOff = true;

                float direction = side == 0 ? -1 : 1;
                foreach (var kind in new[]{UnitKind.Infantry,UnitKind.Archer,UnitKind.Cavalry})
                {
                    float modelX = direction * (kind == UnitKind.Infantry ? 1.35f : kind == UnitKind.Archer ? 5.75f : 3.65f);
                    float modelZ = kind == UnitKind.Cavalry ? -4.3f : -1.7f;
                    var soldier = Soldier(root.transform, label, side == 0, kind,
                        new Vector3(modelX,.85f,modelZ), team, wood, iron, pale, skin, horse);
                    actors.Add(soldier);
                    Export(soldier.gameObject, label + kind);
                }

                var house = Anchor(root.transform,label+" house",new Vector3(x,.9f,2.5f));
                Box(house,new Vector3(2,1.8f,2));
                var actor = house.AddComponent<CombatTarget>();
                var settings = new SerializedObject(actor);
                settings.FindProperty("side").enumValueIndex = side;
                settings.FindProperty("building").enumValueIndex = (int)BuildingKind.House;
                settings.ApplyModifiedPropertiesWithoutUndo();
                actors.Add(actor);
                Block(house.transform,"Stone foundation",new Vector3(0,-.79f,0),new Vector3(1.92f,.22f,1.88f),stone);
                Block(house.transform,"Plaster walls",new Vector3(0,-.25f,0),new Vector3(1.73f,.96f,1.65f),pale);
                Block(house.transform,"Door",new Vector3(0,-.41f,-.838f),new Vector3(.38f,.65f,.045f),wood);
                foreach (float sign in new[]{-1f,1f})
                {
                    Block(house.transform,"Roof slope",new Vector3(sign*.49f,.46f,0),new Vector3(1.13f,.115f,1.94f),team,-sign*28);
                    Block(house.transform,"Timber corner",new Vector3(sign*.79f,-.23f,-.84f),new Vector3(.11f,.93f,.04f),wood);
                    Block(house.transform,"Window",new Vector3(sign*.54f,-.16f,-.84f),new Vector3(.28f,.26f,.045f),iron);
                }
                Block(house.transform,"Gable",new Vector3(0,.33f,0),new Vector3(.58f,.28f,1.64f),pale);
                Block(house.transform,"Ridge beam",new Vector3(0,.73f,0),new Vector3(.13f,.13f,2),wood);
                Block(house.transform,"Chimney",new Vector3(.52f,.57f,.47f),new Vector3(.24f,.56f,.26f),stone);
                Export(house, label + "House");
                foreach (var kind in new[]{BuildingKind.TownHall, BuildingKind.Barracks})
                {
                    var building = Anchor(root.transform, label + " " + kind,
                        new Vector3(direction * (kind == BuildingKind.TownHall ? 6.2f : 9.6f), .9f, 4.5f));
                    CivicBuilding(building.transform, kind, team, pale, stone, wood, iron, gold);
                    Export(building, label + kind);
                }
            }

            var mine = Anchor(root.transform,"Gold deposit", new Vector3(0,.65f,-3.7f));
            var mineBody = Box(mine,new Vector3(2,1.3f,2));
            mine.AddComponent<GoldDeposit>();
            for(int i=0;i<7;i++)
            {
                float angle = i * 2.4f;
                Vector3 p = new Vector3(Mathf.Sin(angle)*.52f,-.34f,Mathf.Cos(angle)*.52f);
                Block(mine.transform,"Fractured rock",p,new Vector3(.62f,.57f,.63f),i%2==0 ? slate : stone, 15+i*12);
                Block(mine.transform,"Gold seam",p+new Vector3(0,.28f,-.12f),new Vector3(.30f,.24f,.27f),gold,22+i*12);
            }
            Block(mine.transform,"Gold crown",new Vector3(.08f,.29f,-.04f),new Vector3(.43f,.40f,.35f),gold,28);

            var wall = Anchor(root.transform,"Stone barricade · 5 × 3",new Vector3(0,1,4.1f));
            var wallBody = Box(wall,new Vector3(5,2,3));
            Block(wall.transform,"Rough stone foundation",new Vector3(0,-.87f,0),new Vector3(4.98f,.25f,2.94f),mortar);
            Block(wall.transform,"Mortar core",new Vector3(0,-.25f,0),new Vector3(4.83f,1.40f,2.76f),mortar);
            Material[] stones = { limestone, sandstone, stone };
            for (int row = 0; row < 4; row++)
            {
                float y = -.77f + row * .35f;
                foreach (float face in new[]{-1f,1f})
                {
                    for (int i = -1; i < 7; i++)
                    {
                        float start = Mathf.Max(-2.49f, -2.49f + (i + (row % 2) * .5f) * .83f);
                        float end = Mathf.Min(2.49f, -2.49f + (i + 1 + (row % 2) * .5f) * .83f);
                        if (end - start < .1f) continue;
                        Block(wall.transform,"Staggered ashlar",new Vector3((start+end)*.5f,y,face*1.39f),
                            new Vector3(end-start-.035f,.315f,.17f),stones[(i+row*2+3)%3]);
                    }
                    for (int i = 0; i < 4; i++)
                        Block(wall.transform,"End return",new Vector3(face*2.39f,y,(i-1.5f)*.69f),
                            new Vector3(.19f,.315f,.65f),stones[(i+row)%3]);
                }
            }
            Block(wall.transform,"Wall walk",new Vector3(0,.42f,0),new Vector3(4.97f,.16f,2.98f),sandstone);
            foreach (float face in new[]{-1f,1f})
                for (int i = 0; i < 4; i++)
                {
                    float x = (i-1.5f)*1.30f;
                    Block(wall.transform,"Stone merlon",new Vector3(x,.71f,face*1.16f),new Vector3(.7f,.44f,.55f),limestone);
                    Block(wall.transform,"Merlon cap",new Vector3(x,.95f,face*1.16f),new Vector3(.76f,.10f,.62f),sandstone);
                }

            Export(mine, "GoldDeposit");
            Export(wall, "Wall");
            var foundation = Anchor(root.transform,"Foundation demonstration",new Vector3(0,.9f,-6.2f));
            Block(foundation.transform,"Stone courses",new Vector3(0,-.70f,0),new Vector3(1.94f,.40f,1.94f),stone);
            Block(foundation.transform,"Unfinished floor",new Vector3(0,-.46f,0),new Vector3(1.76f,.09f,1.76f),dirt);
            foreach (float x in new[]{-.84f,.84f})
                foreach (float z in new[]{-.84f,.84f})
                    Block(foundation.transform,"Scaffold post",new Vector3(x,-.23f,z),new Vector3(.11f,1.34f,.11f),wood);
            foreach (float z in new[]{-.84f,.84f})
            {
                Block(foundation.transform,"Scaffold rail",new Vector3(0,.33f,z),new Vector3(1.8f,.11f,.1f),wood);
                Block(foundation.transform,"Diagonal brace",new Vector3(0,-.10f,z),new Vector3(1.24f,.08f,.09f),wood,29);
            }
            Export(foundation,"Foundation");
            var badge = Anchor(root.transform,"Upgrade detail sample",Vector3.zero);
            Block(badge.transform,"Gold finial",new Vector3(.72f,.90f,-.63f),new Vector3(.22f,.22f,.22f),gold,45);
            Export(badge,"UpgradeBadge");
            badge.transform.SetParent(GameObject.Find("Blue house").transform,false);

            var camera = new GameObject("RTS Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true; camera.orthographicSize = 10;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.10f,.14f,.17f);
            camera.transform.rotation = Quaternion.Euler(55,0,0);
            camera.transform.position = -camera.transform.forward * 22;
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50,-35,0);
            var demo = new GameObject("Style preview controls");
            var fog = demo.AddComponent<MatchVisibility>();
            var fogSettings = new SerializedObject(fog);
            fogSettings.FindProperty("fogTemplate").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/RTS/PrototypeMaterials/FogOfWar.mat");
            fogSettings.ApplyModifiedPropertiesWithoutUndo();
            var preview = new SerializedObject(demo.AddComponent<StylePreview>());
            preview.FindProperty("view").objectReferenceValue = camera;
            preview.FindProperty("fog").objectReferenceValue = fog;
            preview.FindProperty("selection").objectReferenceValue = selection;
            SetArray(preview.FindProperty("models"), actors.ToArray());
            SetArray(preview.FindProperty("landmarks"), new[]{mineBody,wallBody});
            preview.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene,ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static void CivicBuilding(Transform root, BuildingKind kind, Material team,
            Material plaster, Material stone, Material wood, Material iron, Material gold)
        {
            bool hall = kind == BuildingKind.TownHall;
            Block(root,"Stone base",new Vector3(0,-.78f,0),new Vector3(2.93f,.24f,2.9f),stone);
            Block(root,"Central body",new Vector3(0,-.26f,0),new Vector3(2.68f,1.0f,2.53f),hall ? plaster : stone);
            Block(root,"Main entrance",new Vector3(0,-.36f,-1.28f),new Vector3(hall ? .66f : .86f,.83f,.10f),wood);
            foreach (float side in new[]{-1f,1f})
            {
                Block(root,"Corner buttress",new Vector3(side*1.18f,-.25f,-1.29f),new Vector3(.25f,1.05f,.23f),stone);
                if (hall)
                    Block(root,"Sloped roof",new Vector3(side*.70f,.72f,0),
                        new Vector3(1.65f,.15f,2.90f),team,-side*35);
                Block(root,"Upper window",new Vector3(side*.74f,.01f,-1.29f),new Vector3(.24f,.29f,.06f),iron);
            }
            if (hall)
            {
                Block(root,"Roof gable",new Vector3(0,.48f,0),new Vector3(.93f,.55f,2.39f),plaster);
                Block(root,"Ridge",new Vector3(0,1.22f,0),new Vector3(.15f,.12f,2.94f),wood);
                Block(root,"Flag pole",new Vector3(0,1.48f,.02f),new Vector3(.07f,.66f,.07f),wood);
                Block(root,"Team flag",new Vector3(.28f,1.65f,.02f),new Vector3(.58f,.30f,.06f),team);
                Block(root,"Flag trim",new Vector3(.28f,1.79f,.02f),new Vector3(.58f,.05f,.065f),gold);
            }
            else
            {
                Block(root,"Flat roof deck",new Vector3(0,.31f,0),new Vector3(2.8f,.18f,2.82f),iron);
                foreach (float edge in new[]{-1f,1f})
                {
                    Block(root,"Stone parapet",new Vector3(0,.49f,edge*1.29f),new Vector3(2.86f,.22f,.26f),stone);
                    for (int i = -1; i <= 1; i++)
                        Block(root,"Square battlement",new Vector3(i*1.12f,.71f,edge*1.29f),
                            new Vector3(.52f,.37f,.34f),stone);
                    Block(root,"Team pennant",new Vector3(edge*.95f,-.1f,-1.34f),new Vector3(.32f,.73f,.07f),team);
                }
                Block(root,"Shield over door",new Vector3(0,.29f,-1.35f),new Vector3(.44f,.44f,.13f),wood,45);
                Block(root,"Shield boss",new Vector3(0,.29f,-1.43f),new Vector3(.20f,.20f,.08f),gold,45);
                foreach (float side in new[]{-1f,1f})
                    Block(root,"Mounted weapon",new Vector3(side*.56f,.33f,-1.29f),new Vector3(.10f,.53f,.10f),iron,side*28);
            }
        }

        private static void Export(GameObject template, string name)
        {
            var model = new GameObject(name);
            // Copy only geometry. Collider, gameplay and selection components stay on the owning actor.
            foreach (var part in template.GetComponentsInChildren<MeshFilter>())
            {
                var copy = Object.Instantiate(part.gameObject, model.transform, false);
                copy.name = part.name;
            }
            PrefabUtility.SaveAsPrefabAsset(model, Folder + "/Resources/MatchModels/" + name + ".prefab");
            Object.DestroyImmediate(model);
        }

        private static CombatTarget Soldier(Transform root, string label, bool player, UnitKind kind,
            Vector3 position, Material team, Material wood, Material iron, Material pale, Material skin, Material horse)
        {
            var item = Anchor(root, label + " " + kind, position);
            item.transform.rotation = Quaternion.Euler(0, player ? -20 : 20, 0);
            var body = item.AddComponent<CapsuleCollider>();
            body.radius = .4f;
            body.height = 1.7f;
            var settings = new SerializedObject(item.AddComponent<PrototypeUnit>());
            settings.FindProperty("playerOwned").boolValue = player;
            settings.FindProperty("kind").enumValueIndex = (int)kind;
            settings.ApplyModifiedPropertiesWithoutUndo();

            float rider = kind == UnitKind.Cavalry ? .57f : 0;
            Block(item.transform,"Tunic",new Vector3(0,.07f+rider,0),new Vector3(.39f,.50f,.31f),team);
            Block(item.transform,"Face",new Vector3(0,.43f+rider,-.06f),new Vector3(.28f,.26f,.25f),skin);
            Block(item.transform,"Headwear",new Vector3(0,.59f+rider,-.02f),new Vector3(.34f,.18f,.32f),kind == UnitKind.Archer ? pale : iron);
            foreach (float sign in new[]{-1f,1f})
                Block(item.transform,"Boot",new Vector3(sign*.13f,-.61f+rider,0),new Vector3(.17f,.46f,.25f),iron);

            foreach (float sign in new[]{-1f,1f})
                Block(item.transform,"Sleeve",new Vector3(sign*.25f,.04f+rider,0),new Vector3(.14f,.38f,.17f),team,sign*10);

            if (kind == UnitKind.Infantry)
            {
                Block(item.transform,"Broad shield",new Vector3(-.26f,.02f,-.27f),new Vector3(.38f,.59f,.10f),wood,-10);
                Block(item.transform,"Shield boss",new Vector3(-.26f,.04f,-.335f),new Vector3(.17f,.17f,.05f),pale,45);
                Block(item.transform,"Sword",new Vector3(.32f,.04f,-.10f),new Vector3(.10f,.72f,.065f),iron,-30);
            }
            else if (kind == UnitKind.Archer)
            {
                Block(item.transform,"Quiver",new Vector3(-.15f,.16f,.21f),new Vector3(.19f,.61f,.19f),iron,18);
                Block(item.transform,"Bow grip",new Vector3(.32f,.02f,-.21f),new Vector3(.10f,.30f,.10f),wood);
                Block(item.transform,"Upper bow limb",new Vector3(.40f,.26f,-.21f),new Vector3(.09f,.28f,.09f),wood,-30);
                Block(item.transform,"Lower bow limb",new Vector3(.40f,-.22f,-.21f),new Vector3(.09f,.28f,.09f),wood,30);
                Block(item.transform,"Arrow",new Vector3(.35f,.04f,-.39f),new Vector3(.04f,.04f,.59f),pale);
                Block(item.transform,"Bow string",new Vector3(.46f,.02f,-.21f),new Vector3(.026f,.68f,.027f),pale);
            }
            else
            {
                Block(item.transform,"Sword",new Vector3(.32f,.61f,-.10f),new Vector3(.10f,.69f,.065f),iron,-27);
                Block(item.transform,"Horse body",new Vector3(0,-.20f,.04f),new Vector3(.53f,.53f,1.16f),horse);
                Block(item.transform,"Horse neck",new Vector3(0,.04f,-.43f),new Vector3(.31f,.58f,.33f),horse);
                Block(item.transform,"Horse head",new Vector3(0,.30f,-.64f),new Vector3(.30f,.24f,.46f),horse);
                Block(item.transform,"Mane",new Vector3(0,.30f,-.38f),new Vector3(.12f,.19f,.30f),wood);
                Block(item.transform,"Saddle blanket",new Vector3(0,-.01f,.10f),new Vector3(.58f,.23f,.65f),team);
                Block(item.transform,"Tail",new Vector3(0,-.37f,.65f),new Vector3(.12f,.43f,.14f),wood);
                foreach(float x in new[]{-.18f,.18f})
                    foreach(float z in new[]{-.39f,.43f})
                        Block(item.transform,"Horse leg",new Vector3(x,-.60f,z),new Vector3(.14f,.47f,.16f),horse);
            }
            return item.AddComponent<CombatTarget>();
        }

        private static void SetArray(SerializedProperty property, Object[] list)
        {
            property.arraySize = list.Length;
            for(int i=0;i<list.Length;i++) property.GetArrayElementAtIndex(i).objectReferenceValue=list[i];
        }

        private static GameObject Anchor(Transform parent,string label,Vector3 p)
        {
            var item = new GameObject(label);
            item.transform.SetParent(parent,false);
            item.transform.localPosition=p;
            return item;
        }
        private static Collider Box(GameObject item,Vector3 size)
        {
            var box = item.AddComponent<BoxCollider>();
            box.size=size;
            return box;
        }
        private static void Block(Transform parent,string label,Vector3 position,Vector3 size,Material material,float tilt=0)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name=label;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            item.transform.SetParent(parent,false);
            item.transform.localPosition=position;
            item.transform.localScale=size;
            item.transform.localRotation=Quaternion.Euler(0,0,tilt);
            item.GetComponent<Renderer>().sharedMaterial=material;
        }
        private static GameObject Ring(Transform parent,Material material)
        {
            var line = Anchor(parent,"Selection",new Vector3(0,-.82f,0)).AddComponent<LineRenderer>();
            line.useWorldSpace=false; line.loop=true; line.widthMultiplier=.055f;
            line.alignment=LineAlignment.TransformZ; line.transform.localRotation=Quaternion.Euler(90,0,0);
            line.sharedMaterial=material; line.shadowCastingMode=ShadowCastingMode.Off;
            line.positionCount=32;
            for(int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16;
                line.SetPosition(i,new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0)*.55f);
            }
            return line.gameObject;
        }
        private static Material Material(string name,int rgb,bool unlit=false)
        {
            string path=$"{Folder}/{name}.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            material=new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",new Color32((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,255));
            if(!unlit) material.SetFloat("_Smoothness",.12f);
            AssetDatabase.CreateAsset(material,path);
            return material;
        }
    }
}

