using HarmonyLib;

namespace RainCollector.Harmony
{
    /// <summary>
    /// Harmony patches for <see cref="BlockDewCollector"/>.
    /// </summary>
    public class BlockDewCollectorPatches
    {
        /// <summary>
        /// Property name to determine if the dew collector should be a chunk observer.
        /// </summary>
        public const string PropIsChunkObserver = "IsChunkObserver";

        private static bool enabled = false;
        private static bool initialized = false;

        /// <summary>
        /// Patch to
        /// <see cref="BlockDewCollector.addTileEntity(WorldBase, Chunk, Vector3i, BlockValue)"/>
        /// to add a chunk observer as the tile entity is being added.
        /// </summary>
        [HarmonyPatch(typeof(BlockDewCollector), "addTileEntity")]
        public class AddTileEntity
        {
            /// <summary>
            /// Harmony postfix for
            /// <see cref="BlockDewCollector.addTileEntity(WorldBase, Chunk, Vector3i, BlockValue)"/>.
            /// </summary>
            /// <param name="world"></param>
            /// <param name="_blockPos"></param>
            /// <param name="__instance"></param>
            public static void Postfix(
                WorldBase world,
                Vector3i _blockPos,
                BlockDewCollector __instance)
            {
                if (!initialized)
                {
                    Initialize(__instance);
                }
                if (!enabled)
                {
                    return;
                }

                var observer = world.GetGameManager().AddChunkObserver(_blockPos.ToVector3(), false, 3, -1);

                if (RainCollector.Debug)
                {
                    Log.Out($"RainCollector: add observer {observer} at {_blockPos.ToVector3()}");
                }
            }
        }

        /// <summary>
        /// Patch to
        /// <see cref="BlockDewCollector.removeTileEntity(WorldBase, Chunk, Vector3i, BlockValue)"/>
        /// to remove the chunk observer added when the tile entity was added.
        /// </summary>
        [HarmonyPatch(typeof(BlockDewCollector), "removeTileEntity")]
        public class RemoveTileEntity
        {
            /// <summary>
            /// Harmony postfix for
            /// <see cref="BlockDewCollector.removeTileEntity(WorldBase, Chunk, Vector3i, BlockValue)"/>.
            /// </summary>
            /// <param name="world"></param>
            /// <param name="_blockPos"></param>
            /// <param name="__instance"></param>
            public static void Postfix(
                WorldBase world,
                Vector3i _blockPos,
                BlockDewCollector __instance)
            {
                if (!initialized)
                {
                    Initialize(__instance);
                }
                if (!enabled)
                {
                    return;
                }

                if (!(world is World w))
                {
                    return;
                }

                var observedEntities = w.m_ChunkManager.m_ObservedEntities;

                ChunkManager.ChunkObserver observer = null;

                var position = _blockPos.ToVector3();

                for (int i = 0; i < observedEntities.Count; i++)
                {
                    if (observedEntities[i].position == position)
                    {
                        observer = observedEntities[i];
                        break;
                    }
                }

                if (observer != null)
                {
                    world.GetGameManager().RemoveChunkObserver(observer);

                    if (RainCollector.Debug)
                    {
                        Log.Out($"RainCollector: remove observer {observer} at {_blockPos.ToVector3()}");
                    }
                }
            }
        }

        private static void Initialize(BlockDewCollector __instance)
        {
            enabled = TileEntityDewCollectorPatches.HandleUpdate.InitializeProperties(__instance);

            if (enabled)
            {
                // These patches are only enabled if the dew collector should be a chunk observer
                __instance.Properties.ParseBool(PropIsChunkObserver, ref enabled);
            }

            initialized = true;
        }
    }
}
