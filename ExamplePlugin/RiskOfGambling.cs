using BepInEx;
using R2API;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace RiskOfGambling
{
    // This attribute specifies that we have a dependency on a given BepInEx Plugin,
    // We need the R2API ItemAPI dependency because we are using for adding our item to the game.
    // You don't need this if you're not using R2API in your plugin,
    // it's just to tell BepInEx to initialize R2API before this plugin so it's safe to use R2API.
    [BepInDependency(ItemAPI.PluginGUID)]

    // This one is because we use a .language file for language tokens
    // More info in https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Assets/Localization/
    [BepInDependency(LanguageAPI.PluginGUID)]

    // This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    // This is the main declaration of our plugin class.
    // BepInEx searches for all classes inheriting from BaseUnityPlugin to initialize on startup.
    // BaseUnityPlugin itself inherits from MonoBehaviour,
    // so you can use this as a reference for what you can declare and use in your plugin class
    // More information in the Unity Docs: https://docs.unity3d.com/ScriptReference/MonoBehaviour.html
    public class RiskOfGambling : BaseUnityPlugin
    {
        // The Plugin GUID should be a unique ID for this plugin,
        // which is human readable (as it is used in places like the config).
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Xerphy";
        public const string PluginName = "RiskOfGambling";
        public const string PluginVersion = "0.3";

        private GameObject gamblingMachine = PrefabAPI.InstantiateClone(Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Beetle/mdlBeetle.fbx").WaitForCompletion(), "BeebleMemorialStatue");
        private Material gamblingMachineMat = Addressables.LoadAssetAsync<Material>("RoR2/Base/MonstersOnShrineUse/matMonstersOnShrineUse.mat").WaitForCompletion();

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);

            #region Prepping the Asset
            // In-game name
            gamblingMachine.name = "GamblingMachine";

            // Added for for multiplayer compatability
            gamblingMachine.AddComponent<NetworkIdentity>();

            // Scaling the model up
            gamblingMachine.transform.localScale = new Vector3(3f, 3f, 3f);

            // This applies the material to the mesh
            gamblingMachine.transform.GetChild(1).GetComponent<SkinnedMeshRenderer>().sharedMaterial = gamblingMachineMat;

            // Adding a collider so the machine is solid, though it uses a SkinnedMeshRenderer so it doesn't easily work with a MeshCollider, for simplicity sake we're using a simple BoxCollider
            gamblingMachine.transform.GetChild(1).gameObject.AddComponent<BoxCollider>();
            #endregion

            #region Adding interaction
            // Add main necessary component PurchaseInteraction, this will add a Highlight component as well
            PurchaseInteraction interaction = gamblingMachine.AddComponent<PurchaseInteraction>();

            GamblingMachineManager mgr = gamblingMachine.AddComponent<GamblingMachineManager>();

            // What the shrine displays when near
            interaction.contextToken = "Anita Max Wynn";

            // What the shrine displays on ping
            interaction.NetworkdisplayNameToken = "Anita Max Wynn";

            mgr.purchaseInteraction = interaction;

            // The renderer that will be highlighted by our Highlight component
            gamblingMachine.GetComponent<Highlight>().targetRenderer = gamblingMachine.GetComponentInChildren<SkinnedMeshRenderer>();

            // Create a new GameObject that'll act as the trigger
            GameObject trigger = Instantiate(new GameObject("Trigger"), gamblingMachine.transform);

            // Adding a BoxCollider and setting it to be a trigger so it's not solid 
            trigger.AddComponent<BoxCollider>().isTrigger = true;

            // EntityLocator is necessary for the interactable highlight
            trigger.AddComponent<EntityLocator>().entity = gamblingMachine;
            #endregion

            //ShopTerminalBehavior terminalBehavior = gamblingMachine.AddComponent<ShopTerminalBehavior>();

            #region SpawnCard
            InteractableSpawnCard interactableSpawnCard = ScriptableObject.CreateInstance<InteractableSpawnCard>();
            interactableSpawnCard.name = "iscGamblingMachine";
            interactableSpawnCard.prefab = gamblingMachine;
            interactableSpawnCard.sendOverNetwork = true;
            // The size of the interactable, there's Human, Golem, and BeetleQueen
            interactableSpawnCard.hullSize = HullClassification.Golem;
            // Which nodegraph should it spawn on, air or ground
            interactableSpawnCard.nodeGraphType = RoR2.Navigation.MapNodeGroup.GraphType.Ground;
            interactableSpawnCard.requiredFlags = RoR2.Navigation.NodeFlags.None;
            // Nodes have flags that help define what can be spawned on it, any node marked "NoShrineSpawn" shouldn't spawn our shrine on it
            interactableSpawnCard.forbiddenFlags = RoR2.Navigation.NodeFlags.NoShrineSpawn;
            // How much should it cost the director to spawn your interactable
            interactableSpawnCard.directorCreditCost = 0;
            interactableSpawnCard.occupyPosition = true;
            interactableSpawnCard.orientToFloor = false;
            interactableSpawnCard.skipSpawnWhenSacrificeArtifactEnabled = false;
            #endregion

            DirectorCard directorCard = new DirectorCard
            {
                selectionWeight = 1000, // The higher this number the more common it'll be, for reference a normal chest is about 230
                spawnCard = interactableSpawnCard,
            };

            DirectorAPI.DirectorCardHolder directorCardHolder = new DirectorAPI.DirectorCardHolder
            {
                Card = directorCard,
                InteractableCategory = DirectorAPI.InteractableCategory.Shrines
            };

            // Registers the interactable on every stage
            DirectorAPI.Helpers.AddNewInteractable(directorCardHolder);

            /*
            // Or create your stage list and register it on each of those stages
            List<DirectorAPI.Stage> stageList = new List<DirectorAPI.Stage>();

            stageList.Add(DirectorAPI.Stage.DistantRoost);
            stageList.Add(DirectorAPI.Stage.AbyssalDepthsSimulacrum);

            foreach (DirectorAPI.Stage stage in stageList)
            {
                DirectorAPI.Helpers.AddNewInteractableToStage(directorCardHolder, stage);
            }
            */
        }

        // The Update() method is run on every frame of the game.
        private void Update()
        {

        }
    }

    public class GamblingMachineManager : NetworkBehaviour
    {
        public PurchaseInteraction purchaseInteraction;
        private GameObject shrineUseEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/ShrineUseEffect.prefab").WaitForCompletion();
        private Xoroshiro128Plus rng;

        public PickupIndex dropPickup;

        public Transform dropTransform;

        public float dropUpVelocityStrength = 20f;

        public float dropForwardVelocityStrength = 2f;

        private ArrayList rngArray;

        private CostTypeIndex currentCostType;

        public void Start()
        {
            if (NetworkServer.active && Run.instance)
            {
                purchaseInteraction.SetAvailable(true);
            }

            //purchaseInteraction.costType = CostTypeIndex.WhiteItem;
            //purchaseInteraction.automaticallyScaleCostWithDifficulty = true;
            //purchaseInteraction.ShouldShowOnScanner(true);
            //purchaseInteraction.cost = 50;
            purchaseInteraction.onPurchase.AddListener(OnPurchase);
        }

        public void Awake()
        {
            rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            rngArray = new ArrayList();
            dropTransform = base.transform;
            dropPickup = PickupIndex.none;
            currentCostType = CostTypeIndex.None;
        }

        [Server]
        public void OnPurchase(Interactor interactor)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[Server] function 'GamblingMachineManager::OnPurchase(RoR2.Interactor)' called on client");
                return;
            }

            if (!CanBeAffordedByInteractor(interactor))
            {
                return;
            }

            EffectManager.SpawnEffect(shrineUseEffect, new EffectData()
            {
                origin = gameObject.transform.position,
                rotation = Quaternion.identity,
                scale = 3f,
                color = Color.cyan
            }, true);

            CharacterBody character = interactor.GetComponent<CharacterBody>();
            CostTypeDef costTypeDef = RandomizeCostType();
            ItemIndex itemIndex = ItemIndex.None;
            //ShopTerminalBehavior component2 = GetComponent<ShopTerminalBehavior>();

            CostTypeDef.PayCostResults payCostResults = costTypeDef.PayCost(1, interactor, base.gameObject, rng, itemIndex);
            CreateItemTakenOrb(character.corePosition, base.gameObject, payCostResults.itemsTaken[0]);
            ItemDrop();
        }

        public PickupIndex RollDrop()
        {
            int index = rng.RangeInt(1, 101);
            PickupIndex item = PickupIndex.none;

            if (currentCostType.Equals(CostTypeIndex.WhiteItem))
            {
                if (index <= 5)
                {
                    // Don't drop an item
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Boom. T1</color></style>" });
                }
                else if (index >= 86)
                {
                    // Upgrade the item to a green item
                    item = Run.instance.availableTier2DropList[rng.RangeInt(0, Run.instance.availableTier2DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Upgrade! T1</color></style>" });
                }
                else
                {
                    // Reroll the item to another white item
                    item = Run.instance.availableTier1DropList[rng.RangeInt(0, Run.instance.availableTier1DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Reroll! T1</color></style>" });
                }
            }
            else if (currentCostType.Equals(CostTypeIndex.GreenItem))
            {
                if (index <= 20)
                {
                    // Downgrade to a white item
                    item = Run.instance.availableTier1DropList[rng.RangeInt(0, Run.instance.availableTier1DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Downgrade. T2</color></style>" });
                }
                else if (index >= 91)
                {
                    // Upgrade the item to a red item
                    item = Run.instance.availableTier3DropList[rng.RangeInt(0, Run.instance.availableTier3DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Upgrade! T2</color></style>" });
                }
                else
                {
                    // Reroll the item to another green item
                    item = Run.instance.availableTier2DropList[rng.RangeInt(0, Run.instance.availableTier2DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Reroll! T2</color></style>" });
                }
            }
            else if (currentCostType.Equals(CostTypeIndex.RedItem))
            {
                if (index <= 30)
                {
                    // Downgrade to a green item
                    item = Run.instance.availableTier2DropList[rng.RangeInt(0, Run.instance.availableTier2DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Downgrade. T3</color></style>" });
                }
                else
                {
                    // Reroll the item to another green item
                    item = Run.instance.availableTier3DropList[rng.RangeInt(0, Run.instance.availableTier3DropList.Count)];
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage() { baseToken = "<style=cEvent><color=#307FFF>Reroll! T3</color></style>" });
                }
            }

            return item;
        }

        [Server]
        public void ItemDrop()
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[Server] function 'System.Void RoR2.ChestBehavior::ItemDrop()' called on client");
            }

            dropPickup = RollDrop();

            float angle = 360f;
            Vector3 vector = Vector3.up * dropUpVelocityStrength + dropTransform.forward * dropForwardVelocityStrength;
            Quaternion quaternion = Quaternion.AngleAxis(angle, Vector3.up);

            PickupDropletController.CreatePickupDroplet(dropPickup, dropTransform.position + Vector3.up * 1.5f, vector);

            vector = quaternion * vector;

            dropPickup = PickupIndex.none;
            currentCostType = CostTypeIndex.None;
        }

        public bool CanBeAffordedByInteractor(Interactor activator)
        {
            bool hasWhiteItem = false;
            bool hasGreenItem = false;
            bool hasRedItem = false;

            if (CostTypeCatalog.GetCostTypeDef(CostTypeIndex.WhiteItem).IsAffordable(1, activator))
            {
                hasWhiteItem = true;
                rngArray.Add(CostTypeIndex.WhiteItem);
            }

            if (CostTypeCatalog.GetCostTypeDef(CostTypeIndex.GreenItem).IsAffordable(1, activator))
            {
                hasGreenItem = true;
                rngArray.Add(CostTypeIndex.GreenItem);
            }

            if (CostTypeCatalog.GetCostTypeDef(CostTypeIndex.RedItem).IsAffordable(1, activator))
            {
                hasRedItem = true;
                rngArray.Add(CostTypeIndex.RedItem);
            }

            // Determines if the player can afford to gamble (checking if they have at least 1 white, green, or red item). 
            return hasWhiteItem || hasGreenItem || hasRedItem;
        }

        public CostTypeDef RandomizeCostType()
        {
            int index = rng.RangeInt(0, rngArray.Count);
            CostTypeIndex costType = (CostTypeIndex)rngArray[index];
            rngArray.Clear();
            currentCostType = costType;

            return CostTypeCatalog.GetCostTypeDef(costType);
        }

        [Server]
        public static void CreateItemTakenOrb(Vector3 effectOrigin, GameObject targetObject, ItemIndex itemIndex)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[Server] function 'System.Void RoR2.PurchaseInteraction::CreateItemTakenOrb(UnityEngine.Vector3,UnityEngine.GameObject,RoR2.ItemIndex)' called on client");
                return;
            }
            GameObject effectPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OrbEffects/ItemTakenOrbEffect");
            EffectData effectData = new EffectData
            {
                origin = effectOrigin,
                genericFloat = 1.5f,
                genericUInt = (uint)(itemIndex + 1)
            };
            effectData.SetNetworkedObjectReference(targetObject);
            EffectManager.SpawnEffect(effectPrefab, effectData, transmit: true);
        }
    }
}
