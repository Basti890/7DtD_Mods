using HarmonyLib;
using System.Reflection;

namespace RainCollector.Harmony
{
    /// <summary>
    /// Harmony patches for <see cref="TileEntityDewCollector"/>.
    /// </summary>
    public class TileEntityDewCollectorPatches
    {
        /// <summary>
        /// Patches <see cref="TileEntityDewCollector.HandleUpdate(World)"/> to handle the
        /// additional features of this mod.
        /// </summary>
        [HarmonyPatch(typeof(TileEntityDewCollector), nameof(TileEntityDewCollector.HandleUpdate))]
        public class HandleUpdate
        {
            /// <summary>
            /// Fog Convert Multiplier property name.
            /// </summary>
            public const string PropFogConvertMultiplier = "FogConvertMultiplier";

            /// <summary>
            /// Minimum Convert Temperature property name.
            /// </summary>
            public const string PropMinConvertTemperature = "MinConvertTemperature";

            /// <summary>
            /// Rain Convert Multiplier property name.
            /// </summary>
            public const string PropRainConvertMultiplier = "RainConvertMultiplier";

            /// <summary>
            /// Container size property name.
            /// </summary>
            public const string PropContainerSize = "ContainerSize";

            private static bool initialized = false;
            private static bool enabled = false;

            private static float fogConvertMultiplier = 0;
            private static float minConvertTemperature = float.MinValue;
            private static float rainConvertMultiplier = 0;
            private static Vector2i containerSize = Vector2i.zero;

            // This is a reference to the protected TileEntity.setModified method -
            // needed if we add additional fill time and/or create water
            private static MethodInfo setModified = null;

            /// <summary>
            /// Harmony prefix for <see cref="TileEntityDewCollector.HandleUpdate(World)"/>
            /// that saves the last world time as state, and bypasses the method if the temperature
            /// check fails.
            /// </summary>
            /// <param name="world"></param>
            /// <param name="__instance"></param>
            /// <param name="___lastWorldTime"></param>
            /// <param name="__state"></param>
            /// <returns></returns>
            public static bool Prefix(
                World world,
                TileEntityDewCollector __instance,
                ulong ___lastWorldTime,
                out ulong __state)
            {
                __state = ___lastWorldTime;

                if (!initialized)
                {
                    Initialize(__instance);
                }

                // If the XML sets the container size, then do it even if it's otherwise disabled
                if (containerSize != Vector2i.zero && containerSize != __instance.GetContainerSize())
                {
                    SetContainerSize(__instance);
                }

                if (!enabled)
                {
                    return true;
                }

                // If the weather is too cold, don't update anything
                var temperature = WeatherManager.GetTemperature();
                var isWarmEnough = temperature >= minConvertTemperature;

                // The postfix will always run. If we shouldn't add more fill time due to weather,
                // use the current world time as the last world time so the delta time is zero.
                if (!isWarmEnough)
                {
                    __state = world.worldTime;
                }

                if (RainCollector.Debug && !isWarmEnough)
                {
                    Log.Out($@"RainCollector {__instance.localChunkPos
                        }: Temperature is {temperature
                        }, minimum converstion temperature is {minConvertTemperature
                        }, returning {isWarmEnough}");
                }
                    
                return isWarmEnough;
            }

            /// <summary>
            /// Harmony postfix for <see cref="TileEntityDewCollector.HandleUpdate(World)"/>
            /// that handles adding additional fill time due to fog density and rainfall.
            /// </summary>
            /// <param name="world"></param>
            /// <param name="__instance"></param>
            /// <param name="__state"></param>
            public static void Postfix(
                World world,
                TileEntityDewCollector __instance,
                ulong __state)
            {
                if (!enabled)
                {
                    return;
                }

                if (__instance.IsBlocked)
                {
                    return;
                }

                if (__instance.blockValue.Block.IsUnderwater(
                    GameManager.Instance.World,
                    __instance.ToWorldPos(),
                    __instance.blockValue))
                {
                    return;
                }

                // Find how many seconds was added
                var lastWorldTime = __state;

                float deltaTime = (lastWorldTime != 0UL)
                    ? GameUtils.WorldTimeToTotalSeconds(world.worldTime - lastWorldTime)
                    : 0f;

                if (deltaTime == 0f)
                {
                    return;
                }

                var rainfall = WeatherManager.Instance.GetCurrentRainfallValue();
                var fogDensity = SkyManager.GetFogDensity();

                if (fogDensity <= 0 && rainfall <= 0)
                {
                    return;
                }

                var additionalTime = deltaTime * fogDensity * fogConvertMultiplier;
                additionalTime += deltaTime * rainfall * rainConvertMultiplier;

                if (RainCollector.Debug)
                {
                    Log.Out($@"RainCollector {__instance.localChunkPos
                        }: adding {additionalTime} to {deltaTime
                        }. Rainfall = {rainfall} / Fog Density = {fogDensity}");
                }

                var currentIndex = __instance.CurrentIndex;
                if (currentIndex == -1)
                {
                    // Add any extra time to the leftover time, it will be handled next update
                    __instance.leftoverTime += additionalTime;
                    return;
                }

                // Copied nearly verbatim from the vanilla code
                __instance.fillValues[currentIndex] += additionalTime;
                if (__instance.fillValues[currentIndex] >= __instance.CurrentConvertTime)
                {
                    __instance.leftoverTime = __instance.fillValues[currentIndex] - __instance.CurrentConvertTime;
                    __instance.items[currentIndex] = new ItemStack(new ItemValue(__instance.ConvertToItem.Id, false), 1);
                    __instance.fillValues[currentIndex] = -1f;
                    __instance.CurrentConvertTime = -1f;
                    __instance.CurrentIndex = -1;
                }

                // This sends another message over the wire but I don't think it can be avoided.
                setModified?.Invoke(__instance, null);
            }

            /// <summary>
            /// Initializes the mod using the block properties, if it hasn't already been initialized,
            /// and returns true if the mod is enabled.
            /// </summary>
            /// <param name="block"></param>
            /// <returns></returns>
            public static bool InitializeProperties(Block block)
            {
                if (initialized)
                {
                    return enabled;
                }

                // In multiplayer games, this should only be done on the server, otherwise extra
                // fill time will be added for each player
                if (!(SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer
                    || GameManager.IsDedicatedServer))
                {
                    if (RainCollector.Debug)
                    {
                        Log.Out("RainCollector: Calculations must be done on the server in MP games");
                    }

                    return false;
                }

                block.Properties.ParseFloat(PropFogConvertMultiplier, ref fogConvertMultiplier);
                block.Properties.ParseFloat(PropMinConvertTemperature, ref minConvertTemperature);
                block.Properties.ParseFloat(PropRainConvertMultiplier, ref rainConvertMultiplier);

                if (block.Properties.Values.TryGetString(PropContainerSize, out var size))
                {
                    containerSize = StringParsers.ParseVector2i(size);
                }

                if (fogConvertMultiplier == 0 &&
                    rainConvertMultiplier == 0 &&
                    minConvertTemperature == float.MinValue)
                {
                    return false;
                }

                Log.Out($@"RainCollector initialized: {
                    PropFogConvertMultiplier}={fogConvertMultiplier} / {
                    PropRainConvertMultiplier}={rainConvertMultiplier} / {
                    PropMinConvertTemperature}={minConvertTemperature}");

                return true;
            }

            private static void Initialize(TileEntityDewCollector __instance)
            {
                // Call this before setting initialized, otherwise it never sets enabled
                enabled = InitializeProperties(__instance.blockValue.Block);

                if (enabled)
                {
                    setModified = typeof(TileEntity).GetMethod(
                        "setModified",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }

                initialized = true;
            }

            private static void SetContainerSize(TileEntityDewCollector __instance)
            {
                // The SetContainerSize method doesn't change the fill values array
                // (this is probably a TFP bug)
                __instance.fillValues = new float[containerSize.x * containerSize.y];
                __instance.SetContainerSize(containerSize, true);
                if (RainCollector.Debug)
                {
                    Log.Out($@"RainCollector: Container size on {
                        __instance.EntityId} set to {containerSize}");
                }
            }
        }
    }
}
