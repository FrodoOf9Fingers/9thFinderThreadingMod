﻿using Harmony;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace _9thFingerThreadingMod.Replacement_Objects
{
    public static class newRegionTraverser
    {
        public class newBFSWorker
        {
            private Queue<Region> open = new Queue<Region>();

            private int numRegionsProcessed;

            private uint closedIndex = 1u;

            private int closedArrayPos;

            public newBFSWorker(int closedArrayPos)
            {
                this.closedArrayPos = closedArrayPos;
            }

            public void Clear()
            {
                this.open.Clear();
            }

            private void QueueNewOpenRegion(Region region)
            {
                if (region.closedIndex[this.closedArrayPos] == this.closedIndex)
                {
                    throw new InvalidOperationException("Region is already closed; you can't open it. Region: " + region.ToString());
                }
                this.open.Enqueue(region);
                region.closedIndex[this.closedArrayPos] = this.closedIndex;
            }

            private void FinalizeSearch()
            {
            }

            public void BreadthFirstTraverseWork(Region root, RegionEntryPredicate entryCondition, RegionProcessor regionProcessor, int maxRegions, RegionType traversableRegionTypes)
            {
                if ((root.type & traversableRegionTypes) == RegionType.None)
                {
                    return;
                }
                ProfilerThreadCheck.BeginSample("BreadthFirstTraversal");
                this.closedIndex += 1u;
                this.open.Clear();
                this.numRegionsProcessed = 0;
                this.QueueNewOpenRegion(root);
                while (this.open.Count > 0)
                {
                    Region region = this.open.Dequeue();
                    if (DebugViewSettings.drawRegionTraversal)
                    {
                        region.Debug_Notify_Traversed();
                    }
                    ProfilerThreadCheck.BeginSample("regionProcessor");
                    if (regionProcessor != null && regionProcessor(region))
                    {
                        this.FinalizeSearch();
                        ProfilerThreadCheck.EndSample();
                        ProfilerThreadCheck.EndSample();
                        return;
                    }
                    ProfilerThreadCheck.EndSample();
                    this.numRegionsProcessed++;
                    if (this.numRegionsProcessed >= maxRegions)
                    {
                        this.FinalizeSearch();
                        ProfilerThreadCheck.EndSample();
                        return;
                    }
                    for (int i = 0; i < region.links.Count; i++)
                    {
                        RegionLink regionLink = region.links[i];
                        for (int j = 0; j < 2; j++)
                        {
                            Region region2 = regionLink.regions[j];
                            if (region2 != null && region2.closedIndex[this.closedArrayPos] != this.closedIndex && (region2.type & traversableRegionTypes) != RegionType.None && (entryCondition == null || entryCondition(region, region2)))
                            {
                                this.QueueNewOpenRegion(region2);
                            }
                        }
                    }
                }
                this.FinalizeSearch();
                ProfilerThreadCheck.EndSample();
            }
        }


        private static Object _freeWorkers = new ConcurrentQueue<newRegionTraverser.newBFSWorker>();
        private static ConcurrentQueue<newRegionTraverser.newBFSWorker> freeWorkers
        {
            get { return (ConcurrentQueue<newRegionTraverser.newBFSWorker>)_freeWorkers; }
        }


        public static int NumWorkers;

        static newRegionTraverser()
        {
            newRegionTraverser.NumWorkers = 64;
            for (int i = 0; i < newRegionTraverser.NumWorkers; i++)
            {
                newRegionTraverser.freeWorkers.Enqueue(new newRegionTraverser.newBFSWorker(i));
            }
        }

        public static Room FloodAndSetRooms(Region root, Map map, Room existingRoom)
        {
            Room floodingRoom;
            if (existingRoom == null)
            {
                floodingRoom = Room.MakeNew(map);
            }
            else
            {
                floodingRoom = existingRoom;
            }
            root.Room = floodingRoom;
            if (!root.type.AllowsMultipleRegionsPerRoom())
            {
                return floodingRoom;
            }
            RegionEntryPredicate entryCondition = (Region from, Region r) => r.type == root.type && r.Room != floodingRoom;
            RegionProcessor regionProcessor = delegate (Region r)
            {
                r.Room = floodingRoom;
                return false;
            };
            newRegionTraverser.BreadthFirstTraverse(root, entryCondition, regionProcessor, 999999, RegionType.Set_All);
            return floodingRoom;
        }

        public static void FloodAndSetNewRegionIndex(Region root, int newRegionGroupIndex)
        {
            root.newRegionGroupIndex = newRegionGroupIndex;
            if (!root.type.AllowsMultipleRegionsPerRoom())
            {
                return;
            }
            RegionEntryPredicate entryCondition = (Region from, Region r) => r.type == root.type && r.newRegionGroupIndex < 0;
            RegionProcessor regionProcessor = delegate (Region r)
            {
                r.newRegionGroupIndex = newRegionGroupIndex;
                return false;
            };
            newRegionTraverser.BreadthFirstTraverse(root, entryCondition, regionProcessor, 999999, RegionType.Set_All);
        }

        public static bool WithinRegions(this IntVec3 A, IntVec3 B, Map map, int regionLookCount, TraverseParms traverseParams, RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            if (traverseParams.mode == TraverseMode.PassAllDestroyableThings)
            {
                throw new ArgumentException("traverseParams (PassAllDestroyableThings not supported)");
            }
            Region region = A.GetRegion(map, traversableRegionTypes);
            if (region == null)
            {
                return false;
            }
            Region regB = B.GetRegion(map, traversableRegionTypes);
            if (regB == null)
            {
                return false;
            }
            if (region == regB)
            {
                return true;
            }
            RegionEntryPredicate entryCondition = (Region from, Region r) => r.Allows(traverseParams, false);
            bool found = false;
            RegionProcessor regionProcessor = delegate (Region r)
            {
                if (r == regB)
                {
                    found = true;
                    return true;
                }
                return false;
            };
            newRegionTraverser.BreadthFirstTraverse(region, entryCondition, regionProcessor, regionLookCount, traversableRegionTypes);
            return found;
        }

        public static void MarkRegionsBFS(Region root, RegionEntryPredicate entryCondition, int maxRegions, int inRadiusMark, RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            newRegionTraverser.BreadthFirstTraverse(root, entryCondition, delegate (Region r)
            {
                r.mark = inRadiusMark;
                return false;
            }, maxRegions, traversableRegionTypes);
        }

        public static void BreadthFirstTraverse(IntVec3 start, Map map, RegionEntryPredicate entryCondition, RegionProcessor regionProcessor, int maxRegions = 999999, RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            Region region = start.GetRegion(map, traversableRegionTypes);
            if (region == null)
            {
                return;
            }
            newRegionTraverser.BreadthFirstTraverse(region, entryCondition, regionProcessor, maxRegions, traversableRegionTypes);
        }

        public static void BreadthFirstTraverse(Region root, RegionEntryPredicate entryCondition, RegionProcessor regionProcessor, int maxRegions = 999999, RegionType traversableRegionTypes = RegionType.Set_Passable)
        {
            if (newRegionTraverser.freeWorkers.Count == 0)
            {
                Log.Error("No free workers for breadth-first traversal. Either BFS recurred deeper than " + newRegionTraverser.NumWorkers + ", or a bug has put this system in an inconsistent state. Resetting.");
                return;
            }
            if (root == null)
            {
                Log.Error("BreadthFirstTraverse with null root region.");
                return;
            }
            newRegionTraverser.freeWorkers.TryDequeue(out newBFSWorker bFSWorker);
            try
            {
                if (bFSWorker == null)
                    throw new Exception("bfwWorker null");
                bFSWorker.BreadthFirstTraverseWork(root, entryCondition, regionProcessor, maxRegions, traversableRegionTypes);
            }
            catch (Exception ex)
            {
                Log.Error("Exception in BreadthFirstTraverse: " + ex.ToString());
            }
            finally
            {
                bFSWorker.Clear();
                newRegionTraverser.freeWorkers.Enqueue(bFSWorker);
            }
        }
    }
}
