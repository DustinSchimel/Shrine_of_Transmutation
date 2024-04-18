using BepInEx;
using R2API;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using System.Reflection;

namespace ShrineOfTransmutation
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class ShrineOfTransmutation : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Xerphy";
        public const string PluginName = "ShrineOfTransmutation";
        public const string PluginVersion = "1.0.0";

        private AssetBundle mainAssetBundle;
        private GameObject shrineOfTransmutation;

        public void Awake()
        {
            mainAssetBundle = AssetBundle.LoadFromFile(Assembly.GetExecutingAssembly().Location.Replace("ShrineOfTransmutation.dll", "shrineoftransmutationbundle"));

            shrineOfTransmutation = PrefabAPI.InstantiateClone(mainAssetBundle.LoadAsset<GameObject>("Assets/ShrineOfTransmutation/ShrineOfTransmutationAssets/ShrineOfTransmutation.prefab"), "ShrineOfTransmutationModel");

            // Initialize the logging class so that we can properly log for debugging
            Log.Init(Logger);

            #region Prepping the Asset

            // In-game name
            shrineOfTransmutation.name = "ShrineOfTransmutation";

            // Added for for multiplayer compatability
            shrineOfTransmutation.AddComponent<NetworkIdentity>();

            #endregion

            #region Adding interaction

            // Adds the main necessary component PurchaseInteraction, this will add a Highlight component as well
            PurchaseInteraction interaction = shrineOfTransmutation.AddComponent<PurchaseInteraction>();

            ShrineOfTransmutationManager mgr = shrineOfTransmutation.AddComponent<ShrineOfTransmutationManager>();

            String t1ColorHex = ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier1Item);
            String t2ColorHex = ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier2Item);
            String t3ColorHex = ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier3Item);

            // What the shrine displays when near
            interaction.contextToken = "Use Shrine of Transmutation (<color=#" + t1ColorHex + ">1 It</color><color=#" + t2ColorHex + ">em</color><color=#" + t3ColorHex + ">(s)</color><style=cIsDamage>)";

            // What the shrine displays on ping
            interaction.NetworkdisplayNameToken = "Shrine of Transmutation (<color=#" + t1ColorHex + ">1 It</color><color=#" + t2ColorHex + ">em</color><color=#" + t3ColorHex + ">(s)</color><style=cIsDamage>)";

            mgr.purchaseInteraction = interaction;

            // The renderer that will be highlighted by the Highlight component
            shrineOfTransmutation.GetComponent<Highlight>().targetRenderer = shrineOfTransmutation.GetComponentInChildren<MeshRenderer>();

            // EntityLocator is necessary for the interactable highlight
            shrineOfTransmutation.transform.GetChild(0).gameObject.AddComponent<EntityLocator>().entity = shrineOfTransmutation;
            #endregion

            #region SpawnCard

            InteractableSpawnCard interactableSpawnCard = ScriptableObject.CreateInstance<InteractableSpawnCard>();
            interactableSpawnCard.name = "iscShrineOfTransmutation";
            interactableSpawnCard.prefab = shrineOfTransmutation;
            interactableSpawnCard.sendOverNetwork = true;
            // The size of the interactable, there's Human, Golem, and BeetleQueen
            interactableSpawnCard.hullSize = HullClassification.Golem;
            // Which nodegraph should it spawn on, air or ground
            interactableSpawnCard.nodeGraphType = RoR2.Navigation.MapNodeGroup.GraphType.Ground;
            interactableSpawnCard.requiredFlags = RoR2.Navigation.NodeFlags.None;
            // Nodes have flags that help define what can be spawned on it, any node marked "NoShrineSpawn" shouldn't spawn the shrine on it
            interactableSpawnCard.forbiddenFlags = RoR2.Navigation.NodeFlags.NoShrineSpawn;
            // How much should it cost the director to spawn the interactable
            interactableSpawnCard.directorCreditCost = 5;
            interactableSpawnCard.occupyPosition = true;
            interactableSpawnCard.orientToFloor = false;
            interactableSpawnCard.skipSpawnWhenSacrificeArtifactEnabled = false;
            #endregion

            DirectorCard directorCard = new DirectorCard
            {
                selectionWeight = 10, // The higher this number the more common it'll be, for reference a normal chest is about 230
                spawnCard = interactableSpawnCard,
            };

            DirectorAPI.DirectorCardHolder directorCardHolder = new DirectorAPI.DirectorCardHolder
            {
                Card = directorCard,
                InteractableCategory = DirectorAPI.InteractableCategory.Shrines
            };

            // Registers the interactable on every stage
            DirectorAPI.Helpers.AddNewInteractable(directorCardHolder);
        }
    }

    public class ShrineOfTransmutationManager : NetworkBehaviour
    {
        public PurchaseInteraction purchaseInteraction;
        private GameObject shrineUseEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Common/VFX/ShrineUseEffect.prefab").WaitForCompletion();
        private Xoroshiro128Plus rng;

        public PickupIndex dropPickup;

        public Transform dropTransform;

        public float dropUpVelocityStrength = 20f;

        public float dropForwardVelocityStrength = 2.5f;

        private ArrayList rngArrayItems;

        private ArrayList rngArrayScrap;

        private CostTypeIndex currentCostType;

        private ItemDef takenItem;

        public void Start()
        {
            if (NetworkServer.active && Run.instance)
            {
                purchaseInteraction.SetAvailable(true);
            }

            purchaseInteraction.ShouldShowOnScanner();
            purchaseInteraction.onPurchase.AddListener(OnPurchase);
        }

        public void Awake()
        {
            rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            rngArrayItems = new ArrayList();
            rngArrayScrap = new ArrayList();
            dropTransform = base.transform;
            dropPickup = PickupIndex.none;
            currentCostType = CostTypeIndex.None;
        }

        [Server]
        public void OnPurchase(Interactor interactor)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[Server] function 'ShrineOfTransmutationManager::OnPurchase(RoR2.Interactor)' called on client");
                return;
            }

            if (!CanBeAffordedByInteractor(interactor))
            {
                // Add sound effect here
                return;
            }

            purchaseInteraction.SetAvailable(false);

            EffectManager.SpawnEffect(shrineUseEffect, new EffectData()
            {
                origin = gameObject.transform.position,
                rotation = Quaternion.identity,
                scale = 3f,
                color = Color.cyan
            }, true);

            CharacterBody character = interactor.GetComponent<CharacterBody>();
            CostTypeDef costTypeDef = RandomizeCostType(interactor);
            ItemIndex itemIndex = ItemIndex.None;

            CostTypeDef.PayCostResults payCostResults = costTypeDef.PayCost(1, interactor, base.gameObject, rng, itemIndex);
            CreateItemTakenOrb(character.corePosition, base.gameObject, payCostResults.itemsTaken[0]);
            takenItem = ItemCatalog.GetItemDef(payCostResults.itemsTaken[0]);
            StartCoroutine(Delay());
        }

        [Server]
        IEnumerator Delay()
        {
            yield return new WaitForSeconds(1.5f);

            ItemDrop();
            purchaseInteraction.SetAvailable(true);
        }

        public PickupIndex RollDrop()
        {
            int index = rng.RangeInt(1, 1001);
            PickupIndex item = PickupIndex.none;

            // All probabilities. int/10%
            int chanceToBoom = 50;          //5%
            int chanceWhiteToGreen = 150;   //15%
            //int chanceWhiteToWhite = 800;   //80%

            int chanceGreenToWhite = 200;   //20%
            int chanceGreenToRed = 100;     //10%
            //int chanceGreenToGreen = 700;   //70%

            int chanceRedToGreen = 300;     //30%
            //int chanceRedToRed = 700;       //70%

            List<PickupIndex> tier1DropList = Run.instance.availableTier1DropList;
            List<PickupIndex> tier2DropList = Run.instance.availableTier2DropList;
            List<PickupIndex> tier3DropList = Run.instance.availableTier3DropList;

            String whiteColorHex = ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier1Item);
            String greenColorHex = ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier2Item);
            String redColorHex = ColorCatalog.GetColorHexString(ColorCatalog.ColorIndex.Tier3Item);

            if (currentCostType.Equals(CostTypeIndex.WhiteItem))
            {
                if (index <= chanceToBoom)
                {
                    // Don't drop an item
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Destroyed </color><color=#" + whiteColorHex + ">" + Language.GetString(takenItem.nameToken)+ "</color></style>"
                    });
                }
                else if (index >= 1001 - chanceWhiteToGreen)
                {
                    // Upgrade the item to a green item
                    item = tier2DropList[rng.RangeInt(0, tier2DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Upgraded </color><color=#" + whiteColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + greenColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
                }
                else
                {
                    // Reroll the item to another white item
                    item = tier1DropList[rng.RangeInt(0, tier1DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Rerolled </color><color=#" + whiteColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + whiteColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
                }
            }
            else if (currentCostType.Equals(CostTypeIndex.GreenItem))
            {
                if (index <= chanceGreenToWhite)
                {
                    // Downgrade to a white item
                    item = tier1DropList[rng.RangeInt(0, tier1DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Downgraded </color><color=#" + greenColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + whiteColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
                }
                else if (index >= 1001 - chanceGreenToRed)
                {
                    // Upgrade the item to a red item
                    item = tier3DropList[rng.RangeInt(0, tier3DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Upgraded </color><color=#" + greenColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + redColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
                }
                else
                {
                    // Reroll the item to another green item
                    item = tier2DropList[rng.RangeInt(0, tier2DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Rerolled </color><color=#" + greenColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + greenColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
                }
            }
            else if (currentCostType.Equals(CostTypeIndex.RedItem))
            {
                if (index <= chanceRedToGreen)
                {
                    // Downgrade to a green item
                    item = tier2DropList[rng.RangeInt(0, tier2DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Downgraded </color><color=#" + redColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + greenColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
                }
                else
                {
                    // Reroll the item to another red item
                    item = tier3DropList[rng.RangeInt(0, tier3DropList.Count)];

                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage()
                    {
                        baseToken = "<style=cEvent><color=#307FFF>Rerolled </color><color=#" + redColorHex + ">" + Language.GetString(takenItem.nameToken)
                        + "</color><color=#307FFF> to </color><color=#" + redColorHex + ">" + Language.GetString(ItemCatalog.GetItemDef(item.itemIndex).nameToken) + "</color></style>"
                    });
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
                rngArrayItems.Add(CostTypeIndex.WhiteItem);
            }

            if (CostTypeCatalog.GetCostTypeDef(CostTypeIndex.GreenItem).IsAffordable(1, activator))
            {
                hasGreenItem = true;
                rngArrayItems.Add(CostTypeIndex.GreenItem);
            }

            if (CostTypeCatalog.GetCostTypeDef(CostTypeIndex.RedItem).IsAffordable(1, activator))
            {
                hasRedItem = true;
                rngArrayItems.Add(CostTypeIndex.RedItem);
            }

            // Determines if the player can afford to gamble (checking if they have at least 1 white, green, or red item). 
            return hasWhiteItem || hasGreenItem || hasRedItem;
        }

        public bool CheckForScrap(Interactor activator)
        {
            bool hasWhiteScrap = false;
            bool hasGreenScrap = false;
            bool hasRedScrap = false;
            bool hasRegenScrap = false;

            CharacterBody character = activator.GetComponent<CharacterBody>();

            ItemIndex whiteScrap = ItemCatalog.FindItemIndex("ScrapWhite");
            ItemIndex greenScrap = ItemCatalog.FindItemIndex("ScrapGreen");
            ItemIndex redScrap = ItemCatalog.FindItemIndex("ScrapRed");
            ItemIndex regenScrap = ItemCatalog.FindItemIndex("RegeneratingScrap");

            if (character.inventory)
            {
                // Find out how much white scrap we have
                if (character.inventory.GetItemCount(whiteScrap) > 0)
                {
                    hasWhiteScrap = true;
                    rngArrayScrap.Add(CostTypeIndex.WhiteItem);
                }

                // Find out how much green scrap we have
                if (character.inventory.GetItemCount(greenScrap) > 0)
                {
                    hasGreenScrap = true;
                    rngArrayScrap.Add(CostTypeIndex.GreenItem);
                }

                // Find out how much red scrap we have
                if (character.inventory.GetItemCount(redScrap) > 0)
                {
                    hasRedScrap = true;
                    rngArrayScrap.Add(CostTypeIndex.RedItem);
                }

                // Find out how much regenerating scrap we have
                if (character.inventory.GetItemCount(regenScrap) > 0)
                {
                    hasRegenScrap = true;
                    // Only add regen scrap as an option if green scrap was not already in there, since adding both could make it favor using green scrap
                    if (!rngArrayScrap.Contains(CostTypeIndex.GreenItem))
                    {
                        rngArrayScrap.Add(CostTypeIndex.GreenItem);
                    }
                }
            }

            // Determines if the player can afford to use scrap (checking if they have at least 1 white, green, red or regenerating scrap). 
            return hasWhiteScrap || hasGreenScrap || hasRedScrap || hasRegenScrap;
        }


        public CostTypeDef RandomizeCostType(Interactor activator)
        {
            int index;
            CostTypeIndex costType;

            // If player has scrap, use that instead. If they have multiple of different colors, pick one randomly. Else, just use an item of random color.
            if (CheckForScrap(activator))
            {
                index = rng.RangeInt(0, rngArrayScrap.Count);
                costType = (CostTypeIndex)rngArrayScrap[index];
            }
            else
            {
                index = rng.RangeInt(0, rngArrayItems.Count);
                costType = (CostTypeIndex)rngArrayItems[index];
            }

            rngArrayItems.Clear();
            rngArrayScrap.Clear();
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