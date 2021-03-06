using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

#pragma warning disable CS0618 // InventoryItem is obsolete

namespace NamespacedItems.Plugin
{
    static partial class Implementations
    {
        static readonly Dictionary<(string nspace, string name), INamespacedItem> CachedItems = new();
        static readonly Dictionary<ArmorComponent, IArmorSet> CachedArmors = new();

        static AssemblyBuilder asm;
        static ModuleBuilder module;

        public static INamespacedItem ToNamespacedItem(InventoryItem item, string nspace, string name)
        {
            if (CachedItems.TryGetValue((nspace, name), out var cached)) return cached;
            if (module is null)
            {
                var asmname = new AssemblyName("NamespacedGeneratedItems");
                asm = AppDomain.CurrentDomain.DefineDynamicAssembly(asmname, AssemblyBuilderAccess.RunAndSave);
                module = asm.DefineDynamicModule(asmname.Name, asmname.Name + ".dll");
            }

            var original = AccessTools.Field(typeof(BaseNamespacedGeneratedItem), nameof(BaseNamespacedGeneratedItem.original));

            var isFood = item.tag == InventoryItem.ItemTag.Food;
            var isFuel = item.tag == InventoryItem.ItemTag.Fuel;
            var isArmor = item.armorComponent != null;
            var isBow = item.bowComponent != null;
            var isArrow = item.tag == InventoryItem.ItemTag.Arrow;
            var isBuildable = item.buildable;
            var isEnemyProjectile = false;
            if (item.prefab != null && item.prefab.TryGetComponent<EnemyProjectile>(out _))
            {
                isBow = false;
                isArrow = false;
                isEnemyProjectile = true;
            }

            var isResourceHarvest = (item.type & (InventoryItem.ItemType.Axe | InventoryItem.ItemType.Pickaxe)) != 0;
            var isMelee = (item.attackDamage > 1 && !isArrow) || isResourceHarvest;

            var type = module.DefineType($"{nspace}.{name}", TypeAttributes.Public, typeof(BaseNamespacedGeneratedItem), Type.EmptyTypes);

            if (isFood)
            {
                type.AddInterfaceImplementation(typeof(IFoodItem));
                type.AddGetProperty(nameof(IFoodItem.HealthRegen), typeof(float)).OriginalField(nameof(InventoryItem.heal));
                type.AddGetProperty(nameof(IFoodItem.HungerRegen), typeof(float)).OriginalField(nameof(InventoryItem.hunger));
                type.AddGetProperty(nameof(IFoodItem.StaminaRegen), typeof(float)).OriginalField(nameof(InventoryItem.stamina));
            }
            if (isFuel)
            {
                type.AddInterfaceImplementation(typeof(IFuelItem));
                type.AddGetProperty(nameof(IFuelItem.MaxUses), typeof(int)).OriginalField(nameof(InventoryItem.fuel), nameof(InventoryItem.fuel.maxUses));
                type.AddAutoProperty(nameof(IFuelItem.CurrentUses), typeof(int));
                type.AddGetProperty(nameof(IFuelItem.SpeedMultiplier), typeof(int)).OriginalField(nameof(InventoryItem.fuel), nameof(InventoryItem.fuel.speedMultiplier));
            }

            FieldInfo armorSetField = null;
            if (isArmor)
            {
                type.AddInterfaceImplementation(typeof(IArmorItem));
                type.AddGetProperty(nameof(IArmorItem.Armor), typeof(int)).OriginalField(nameof(InventoryItem.armor));

                {
                    var il = type.AddGetProperty(nameof(IArmorItem.Slot), typeof(ArmorSlot)).GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, original);
                    il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(InventoryItem), nameof(InventoryItem.tag)));
                    il.Emit(OpCodes.Ldc_I4_4);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Ret);
                }

                if (item.armorComponent == null || item.armorComponent.name == "NormalArmor")
                {
                    var il = type.AddGetProperty(nameof(IArmorItem.Set), typeof(IArmorSet)).GetILGenerator();
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                }
                else
                {
                    if (!CachedArmors.ContainsKey(item.armorComponent))
                    {
                        if (GeneratedArmorSet.sets.TryGetValue(item.armorComponent.name, out var setName))
                        {
                            CachedArmors[item.armorComponent] = new GeneratedArmorSet(setName, item.armorComponent.setBonus);
                        }
                        else
                        {
                            throw new InvalidOperationException($"No set name found. Please add the key '{item.armorComponent.name}' to GeneratedArmorSet.sets.");
                        }
                    }
                    armorSetField = type.DefineField("armorSet", typeof(IArmorSet), FieldAttributes.Private);
                    var il = type.AddGetProperty(nameof(IArmorItem.Set), typeof(IArmorSet)).GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, armorSetField);
                    il.Emit(OpCodes.Ret);
                }
            }
            if (isBow)
            {
                type.AddInterfaceImplementation(typeof(IBowItem));
                type.AddGetProperty(nameof(IBowItem.ProjectileSpeed), typeof(float)).OriginalField(nameof(InventoryItem.bowComponent), nameof(InventoryItem.bowComponent.projectileSpeed));
                type.AddGetProperty(nameof(IBowItem.ArrowCount), typeof(int)).OriginalField(nameof(InventoryItem.bowComponent), nameof(InventoryItem.bowComponent.nArrows));
                var il = type.AddGetProperty(nameof(IBowItem.ArrowAngleDelta), typeof(float)).GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, original);
                il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(InventoryItem), nameof(InventoryItem.bowComponent)));
                il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(BowComponent), nameof(BowComponent.angleDelta)));
                il.Emit(OpCodes.Conv_R4);
                il.Emit(OpCodes.Ret);
            }
            if (isArrow)
            {
                type.AddInterfaceImplementation(typeof(IArrowItem));
                type.AddGetProperty(nameof(IArrowItem.AttackDamage), typeof(float)).OriginalField(nameof(InventoryItem.attackDamage));
                type.AddGetProperty(nameof(IArrowItem.Prefab), typeof(GameObject)).OriginalField(nameof(InventoryItem.prefab));
                type.AddGetProperty(nameof(IArrowItem.ArrowMaterial), typeof(Material)).OriginalField(nameof(InventoryItem.material));
            }
            if (isEnemyProjectile)
            {
                type.AddInterfaceImplementation(typeof(IEnemyProjectileItem));
                type.AddGetProperty(nameof(IEnemyProjectileItem.Prefab), typeof(GameObject)).OriginalField(nameof(InventoryItem.prefab));
                type.AddGetProperty(nameof(IEnemyProjectileItem.AttackDamage), typeof(float)).OriginalField(nameof(InventoryItem.attackDamage));
                type.AddGetProperty(nameof(IEnemyProjectileItem.ProjectileSpeed), typeof(float)).OriginalField(nameof(InventoryItem.bowComponent), nameof(InventoryItem.bowComponent.projectileSpeed));
                type.AddGetProperty(nameof(IEnemyProjectileItem.ColliderDisabledTime), typeof(float)).OriginalField(nameof(InventoryItem.bowComponent), nameof(InventoryItem.bowComponent.colliderDisabledTime));
                type.AddGetProperty(nameof(IEnemyProjectileItem.RotationOffset), typeof(Vector3)).OriginalField(nameof(InventoryItem.rotationOffset));
            }
            if (isBuildable)
            {
                type.AddInterfaceImplementation(typeof(IBuildableItem));
                type.AddGetProperty(nameof(IBuildableItem.Prefab), typeof(GameObject)).OriginalField(nameof(InventoryItem.prefab));
                type.AddGetProperty(nameof(IBuildableItem.SnapToGrid), typeof(bool)).OriginalField(nameof(InventoryItem.grid));
                type.AddGetProperty(nameof(IBuildableItem.GhostMesh), typeof(Mesh)).OriginalField(nameof(InventoryItem.mesh));
                type.AddGetProperty(nameof(IBuildableItem.GhostMaterial), typeof(Material)).OriginalField(nameof(InventoryItem.material));
            }
            if (isMelee)
            {
                type.AddInterfaceImplementation(typeof(IMeleeItem));
                type.AddGetProperty(nameof(IMeleeItem.AttackDamage), typeof(float)).OriginalField(nameof(InventoryItem.attackDamage));
                type.AddGetProperty(nameof(IMeleeItem.AttackRange), typeof(float)).OriginalField(nameof(InventoryItem.attackRange));
                type.AddGetProperty(nameof(IMeleeItem.AttackTypes), typeof(IEnumerable<MobType.Weakness>)).OriginalField(nameof(InventoryItem.attackTypes));
            }
            if (isResourceHarvest)
            {
                type.AddInterfaceImplementation(typeof(IResourceHarvestItem));
                type.AddGetProperty(nameof(IResourceHarvestItem.ResourceDamage), typeof(float)).OriginalField(nameof(InventoryItem.resourceDamage));
                var il = type.AddGetProperty(nameof(IResourceHarvestItem.HarvestType), typeof(HarvestTool)).GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, original);
                il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(InventoryItem), nameof(InventoryItem.type)));
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Ret);
            }

            var ctor = type.DefineConstructor(
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName,
                CallingConventions.Standard,
                new[] { typeof(InventoryItem), typeof(string), typeof(string) });
            {
                var il = ctor.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, AccessTools.Constructor(typeof(BaseNamespacedGeneratedItem), new[] { typeof(InventoryItem), typeof(string), typeof(string) }));
                il.Emit(OpCodes.Ret);
            }

            var copy = type.DefineMethod(
                nameof(INamespacedItem.Copy),
                MethodAttributes.Public |
                MethodAttributes.Final |
                MethodAttributes.HideBySig |
                MethodAttributes.Virtual,
                typeof(INamespacedItem), Type.EmptyTypes);
            {
                var il = copy.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, original);
                il.Emit(OpCodes.Ldarg_0);
                il.EmitCall(OpCodes.Call, AccessTools.PropertyGetter(typeof(BaseNamespacedGeneratedItem), nameof(BaseNamespacedGeneratedItem.Namespace)), null);
                il.Emit(OpCodes.Ldarg_0);
                il.EmitCall(OpCodes.Call, AccessTools.PropertyGetter(typeof(BaseNamespacedGeneratedItem), nameof(BaseNamespacedGeneratedItem.Name)), null);
                il.Emit(OpCodes.Newobj, ctor);
                if (armorSetField is not null)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, armorSetField);
                    il.Emit(OpCodes.Stfld, armorSetField);
                }
                il.Emit(OpCodes.Ret);
            }

            var resultType = type.CreateType();
            var resultCtor = AccessTools.Constructor(resultType, new[] { typeof(InventoryItem), typeof(string), typeof(string) });
            var result = (INamespacedItem)resultCtor.Invoke(new object[] { item, nspace, name });
            if (armorSetField != null)
            {
                armorSetField.SetValue(result, CachedArmors[item.armorComponent]);
            }
            CachedItems[(nspace, name)] = result;
            return result;
        }

        static MethodBuilder AddGetProperty(this TypeBuilder type, string name, Type returnType)
        {
            var prop = type.DefineProperty(name, PropertyAttributes.None, returnType, Type.EmptyTypes);
            var get = type.DefineMethod(
                $"get_{name}",
                MethodAttributes.Public |
                MethodAttributes.Final |
                MethodAttributes.HideBySig |
                MethodAttributes.SpecialName |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                returnType, Type.EmptyTypes);
            prop.SetGetMethod(get);
            return get;
        }

        static void AddAutoProperty(this TypeBuilder type, string name, Type returnType)
        {
            var field = type.DefineField($"<{name}>k__BackingField", returnType, FieldAttributes.Private);
            var get = type.DefineMethod(
                $"get_{name}",
                MethodAttributes.Public |
                MethodAttributes.Final |
                MethodAttributes.HideBySig |
                MethodAttributes.SpecialName |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                returnType, Type.EmptyTypes);
            var set = type.DefineMethod(
                $"set_{name}",
                MethodAttributes.Public |
                MethodAttributes.Final |
                MethodAttributes.HideBySig |
                MethodAttributes.SpecialName |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                typeof(void), new[] { returnType });
            field.SetCustomAttribute(AccessTools.Constructor(typeof(CompilerGeneratedAttribute), Type.EmptyTypes), new byte[] { 1, 0, 0, 0 });
            get.SetCustomAttribute(AccessTools.Constructor(typeof(CompilerGeneratedAttribute), Type.EmptyTypes), new byte[] { 1, 0, 0, 0 });
            set.SetCustomAttribute(AccessTools.Constructor(typeof(CompilerGeneratedAttribute), Type.EmptyTypes), new byte[] { 1, 0, 0, 0 });
            var getil = get.GetILGenerator();
            getil.Emit(OpCodes.Ldarg_0);
            getil.Emit(OpCodes.Ldfld, field);
            getil.Emit(OpCodes.Ret);
            var setil = set.GetILGenerator();
            setil.Emit(OpCodes.Ldarg_0);
            setil.Emit(OpCodes.Ldarg_1);
            setil.Emit(OpCodes.Stfld, field);
            setil.Emit(OpCodes.Ret);

            var prop = type.DefineProperty(name, PropertyAttributes.None, returnType, Type.EmptyTypes);
            prop.SetGetMethod(get);
            prop.SetSetMethod(set);
        }

        static void OriginalField(this MethodBuilder method, params string[] names)
        {
            var original = AccessTools.Field(typeof(BaseNamespacedGeneratedItem), nameof(BaseNamespacedGeneratedItem.original));
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, original);
            var lastType = original.FieldType;
            foreach (var name in names)
            {
                var field = AccessTools.Field(lastType, name);
                il.Emit(OpCodes.Ldfld, field);
                lastType = field.FieldType;
            }
            il.Emit(OpCodes.Ret);
        }
    }

    public abstract class BaseNamespacedGeneratedItem : INamespacedItem
    {
        protected internal readonly InventoryItem original;

        public string Namespace { get; }
        public string Name { get; }
        public string DisplayName => original.name;
        public string Description => original.description;
        public int Amount { get; set; }
        public int MaxAmount => original.max;
        public bool Stackable => original.stackable;
        public bool CanDespawn => !original.important;
        public Sprite Sprite => original.sprite;
        public Mesh DroppedMesh => original.mesh;
        public Material DroppedMaterial => original.material;
        public Vector3 HeldRotationOffset => original.rotationOffset;
        public Vector3 HeldPositionOffset => original.positionOffset;
        public float HeldScale => original.scale;

        protected BaseNamespacedGeneratedItem(InventoryItem item, string nspace, string name)
        {
            original = item;
            Namespace = nspace;
            Name = name;
        }

        public abstract INamespacedItem Copy();

        public static readonly Dictionary<int, string> names = new()
        {
            [0] = "bark",
            [1] = "chest",
            [2] = "coal",
            [3] = "coin",
            [4] = "flint",
            [5] = "adamantite_boots",
            [6] = "chunkium_boots",
            [7] = "gold_boots",
            [8] = "mithril_boots",
            [9] = "obamium_boots",
            [10] = "steel_boots",
            [11] = "wolfskin_boots",
            [12] = "adamantite_helmet",
            [13] = "chunkium_helmet",
            [14] = "gold_helmet",
            [15] = "mithril_helmet",
            [16] = "obamium_helmet",
            [17] = "steel_helmet",
            [18] = "wolfskin_helmet",
            [19] = "adamantite_pants",
            [20] = "chunkium_pants",
            [21] = "gold_pants",
            [22] = "mithril_pants",
            [23] = "obamium_pants",
            [24] = "steel_pants",
            [25] = "wolfskin_pants",
            [26] = "adamantite_chestplate",
            [27] = "chunkium_chestplate",
            [28] = "gold_chestplate",
            [29] = "mithril_chestplate",
            [30] = "obamium_chestplate",
            [31] = "steel_chestplate",
            [32] = "wolfskin_chestplate",
            [33] = "wood_window",
            [34] = "wood_doorway",
            [35] = "wood_floor",
            [36] = "wood_pole",
            [37] = "wood_pole_half",
            [38] = "wood_roof",
            [39] = "wood_stairs",
            [40] = "wood_stairs_thinn",
            [41] = "wood_wall",
            [42] = "wood_wall_half",
            [43] = "wood_wall_tilted",
            [44] = "torch",
            [45] = "red_apple",
            [46] = "bowl",
            [47] = "dough",
            [48] = "flax_fibers",
            [49] = "flax",
            [50] = "raw_meat",
            [51] = "gulpon_shroom",
            [52] = "ligon_shroom",
            [53] = "slurbon_shroom",
            [54] = "sugon_shroom",
            [55] = "wheat",
            [56] = "bread",
            [57] = "cooked_meat",
            [58] = "apple_pie",
            [59] = "meat_pie",
            [60] = "meat_soup",
            [61] = "purple_soup",
            [62] = "red_soup",
            [63] = "weird_soup",
            [64] = "yellow_soup",
            [65] = "ancientcore",
            [66] = "adamantite_bar",
            [67] = "chunkium_bar",
            [68] = "gold_bar",
            [69] = "iron_bar",
            [70] = "mithril_bar",
            [71] = "obamium_bar",
            [72] = "ancient_bone",
            [73] = "dragonball",
            [74] = "fireball",
            [75] = "lightningball",
            [76] = "rock_projectile",
            [77] = "rock_projectile_roll",
            [78] = "spear_projectile",
            [79] = "spike_attack",
            [80] = "sword_projectile",
            [81] = "waterball",
            [82] = "windball",
            [83] = "adamantite_ore",
            [84] = "chunkium_ore",
            [85] = "gold_ore",
            [86] = "iron_ore",
            [87] = "mithril_ore",
            [88] = "obamium_ore",
            [89] = "ruby",
            [90] = "rock",
            [91] = "birch_wood",
            [92] = "dark_oak_wood",
            [93] = "fir_wood",
            [94] = "wood",
            [95] = "oak_wood",
            [96] = "anvil",
            [97] = "cauldron",
            [98] = "fletching_table",
            [99] = "furnace",
            [100] = "workbench",
            [101] = "boat_map",
            [102] = "gem_map",
            [103] = "blue_gem",
            [104] = "green_gem",
            [105] = "pink_gem",
            [106] = "red_gem",
            [107] = "yellow_gem",
            [108] = "adamantite_axe",
            [109] = "gold_axe",
            [110] = "mithril_axe",
            [111] = "steel_axe",
            [112] = "wood_axe",
            [113] = "oak_bow",
            [114] = "wood_bow",
            [115] = "birch_bow",
            [116] = "fir_bow",
            [117] = "ancient_bow",
            [118] = "adamantite_pickaxe",
            [119] = "gold_pickaxe",
            [120] = "mithril_pickaxe",
            [121] = "steel_pickaxe",
            [122] = "wood_pickaxe",
            [123] = "rope",
            [124] = "shovel",
            [125] = "adamantite_sword",
            [126] = "gold_sword",
            [127] = "mithril_sword",
            [128] = "obamium_sword",
            [129] = "steel_sword",
            [130] = "milk",
            [131] = "adamantite_arrow",
            [132] = "fire_arrow",
            [133] = "flint_arrow",
            [134] = "lightning_arrow",
            [135] = "mithril_arrow",
            [136] = "steel_arrow",
            [137] = "water_arrow",
            [138] = "chiefs_spear",
            [139] = "chunky_hammer",
            [140] = "gronks_sword",
            [141] = "gronks_sword_projectile",
            [142] = "night_blade",
            [143] = "wyvern_dagger",
            [144] = "black_shard",
            [145] = "blade",
            [146] = "hammer_shaft",
            [147] = "spear_tip",
            [148] = "sword_hilt",
            [149] = "wolf_claws",
            [150] = "wolfskin",
            [151] = "wyvern_claws",
        };
    }

    public class GeneratedArmorSet : IArmorSet
    {
        public static Dictionary<string, string> sets = new()
        {
            ["ChunkiumArmor"] = "chunkium_armor",
            ["WolfArmor"] = "wolf_armor",
        };

        public string Namespace => "muck";
        public string Name { get; }
        public string Bonus { get; }

        public IArmorItem Helmet { get; internal set; }
        public IArmorItem Torso { get; internal set; }
        public IArmorItem Legs { get; internal set; }
        public IArmorItem Feet { get; internal set; }

        internal GeneratedArmorSet(string name, string bonus)
        {
            Name = name;
            Bonus = bonus;
        }
    }
}