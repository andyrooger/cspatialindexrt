//   RTree.java
//   Java Spatial Index Library
//   Copyright (C) 2002 Infomatiq Limited
//   Copyright (C) 2008 Aled Morris aled@sourceforge.net
//  
//  This library is free software; you can redistribute it and/or
//  modify it under the terms of the GNU Lesser General Public
//  License as published by the Free Software Foundation; either
//  version 2.1 of the License, or (at your option) any later version.
//  
//  This library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//  Lesser General Public License for more details.
//  
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//  Ported to C# By Dror Gluska, April 9th, 2009

using System;
using System.Collections.Generic;
using System.Linq;

namespace RTree
{

    /// <summary>
    /// This is a lightweight RTree implementation, specifically designed 
    /// for the following features (in order of importance): 
    ///
    /// Fast intersection query performance. To achieve this, the RTree 
    /// uses only main memory to store entries. Obviously this will only improve
    /// performance if there is enough physical memory to avoid paging.
    /// Low memory requirements.
    /// Fast add performance.
    ///
    ///
    /// The main reason for the high speed of this RTree implementation is the 
    /// avoidance of the creation of unnecessary objects, mainly achieved by using
    /// primitive collections from the trove4j library.
    /// author aled@sourceforge.net
    /// version 1.0b2p1
    /// Ported to C# By Dror Gluska, April 9th, 2009
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class RTree<T>
    {
        const string version = "1.0b2p1";

        // parameters of the tree
        const int DEFAULT_MAX_NODE_ENTRIES = 10;
        internal int maxNodeEntries;
        int minNodeEntries;

        Dictionary<int, Node<T>> nodeMap = new Dictionary<int, Node<T>>();

        // used to mark the status of entries during a Node&lt;T&gt; split
        const int ENTRY_STATUS_ASSIGNED = 0;
        const int ENTRY_STATUS_UNASSIGNED = 1;
        byte[] entryStatus;
        byte[] initialEntryStatus;

        // stacks used to store nodeId and entry index of each Node&lt;T&gt;
        // from the root down to the leaf. Enables fast lookup
        // of nodes when a split is propagated up the tree.
        //private TIntStack parents = new TIntStack();
        Stack<int> parents = new Stack<int>();
        //private TIntStack parentsEntry = new TIntStack();
        Stack<int> parentsEntry = new Stack<int>();

        // initialisation
        int treeHeight = 1; // leaves are always level 1
        int rootNodeId;
        int msize;

        // Enables creation of new nodes
        //private int highestUsedNodeId = rootNodeId; 
        int highestUsedNodeId;

        // Deleted Node&lt;T&gt; objects are retained in the nodeMap, 
        // so that they can be reused. Store the IDs of nodes
        // which can be reused.
        //private TIntStack deletedNodeIds = new TIntStack();
        Stack<int> deletedNodeIds = new Stack<int>();

        // List of nearest rectangles. Use a member variable to
        // avoid recreating the object each time nearest() is called.
        //private TIntArrayList nearestIds = new TIntArrayList();
        List<int> nearestIds = new List<int>();

        //Added dictionaries to support generic objects..
        //possibility to change the code to support objects without dictionaries.
        Dictionary<int, T> IdsToItems = new Dictionary<int,T>();
        Dictionary<T,int> ItemsToIds = new Dictionary<T,int>();
        volatile int idcounter = int.MinValue;

        public ICollection<T> All => ItemsToIds.Keys;
        public bool ContainsKey(T key) => ItemsToIds.ContainsKey(key);

        public RTree()
        {
            init();
        }
        
        /// <summary>
        /// Initialize implementation dependent properties of the RTree.
        /// </summary>
        /// <param name="MaxNodeEntries">his specifies the maximum number of entries
        ///in a node. The default value is 10, which is used if the property is
        ///not specified, or is less than 2.</param>
        /// <param name="MinNodeEntries">This specifies the minimum number of entries
        ///in a node. The default value is half of the MaxNodeEntries value (rounded
        ///down), which is used if the property is not specified or is less than 1.
        ///</param>
        public RTree(int MaxNodeEntries, int MinNodeEntries)
        {
            minNodeEntries = MinNodeEntries;
            maxNodeEntries = MaxNodeEntries;
            init();
        }
        void init()
        {
            // Obviously a Node&lt;T&gt; with less than 2 entries cannot be split.
            // The Node&lt;T&gt; splitting algorithm will work with only 2 entries
            // per node, but will be inefficient.
            if (maxNodeEntries < 2)
            {
                maxNodeEntries = DEFAULT_MAX_NODE_ENTRIES;
            }

            // The MinNodeEntries must be less than or equal to (int) (MaxNodeEntries / 2)
            if (minNodeEntries < 1 || minNodeEntries > maxNodeEntries / 2)
            {
                minNodeEntries = maxNodeEntries / 2;
            }

            entryStatus = new byte[maxNodeEntries];
            initialEntryStatus = new byte[maxNodeEntries];

            for (var i = 0; i < maxNodeEntries; i++)
            {
                initialEntryStatus[i] = ENTRY_STATUS_UNASSIGNED;
            }

            var root = new Node<T>(rootNodeId, 1, maxNodeEntries);
            nodeMap.Add(rootNodeId, root);
        }

        /// <summary>
        /// Adds an item to the spatial index
        /// </summary>
        /// <param name="r"></param>
        /// <param name="item"></param>
        public void Add(Rectangle r, T item)
        {
            idcounter++;
            var id = idcounter;

            IdsToItems.Add(id, item);
            ItemsToIds.Add(item, id);

            add(r, id);
        }
        void add(Rectangle r, int id)
        {
            add(r.copy(), id, 1);
            msize++;
        }

        /// <summary>
        /// Adds a new entry at a specified level in the tree
        /// </summary>
        /// <param name="r"></param>
        /// <param name="id"></param>
        /// <param name="level"></param>
        void add(Rectangle r, int id, int level)
        {
            // I1 [Find position for new record] Invoke ChooseLeaf to select a 
            // leaf Node&lt;T&gt; L in which to place r
            var n = chooseNode(r, level);
            Node<T> newLeaf = null;

            // I2 [Add record to leaf node] If L has room for another entry, 
            // install E. Otherwise invoke SplitNode to obtain L and LL containing
            // E and all the old entries of L
            if (n.entryCount < maxNodeEntries)
            {
                n.addEntryNoCopy(r, id);
            }
            else
            {
                newLeaf = splitNode(n, r, id);
            }

            // I3 [Propagate changes upwards] Invoke AdjustTree on L, also passing LL
            // if a split was performed
            var newNode = adjustTree(n, newLeaf);

            // I4 [Grow tree taller] If Node&lt;T&gt; split propagation caused the root to 
            // split, create a new root whose children are the two resulting nodes.
            if (newNode != null)
            {
                var oldRootNodeId = rootNodeId;
                var oldRoot = getNode(oldRootNodeId);

                rootNodeId = getNextNodeId();
                treeHeight++;
                var root = new Node<T>(rootNodeId, treeHeight, maxNodeEntries);
                root.addEntry(newNode.mbr, newNode.nodeId);
                root.addEntry(oldRoot.mbr, oldRoot.nodeId);
                nodeMap[rootNodeId] =  root;
            }
        }

        /// <summary>
        /// Deletes an item from the spatial index
        /// </summary>
        /// <param name="r"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Delete(Rectangle r, T item)
        {
            var id = ItemsToIds[item];

            var success = delete(r, id);
            if (success)
            {
                IdsToItems.Remove(id);
                ItemsToIds.Remove(item);
            }
            return success;
        }
        bool delete(Rectangle r, int id)
        {
            // FindLeaf algorithm inlined here. Note the "official" algorithm 
            // searches all overlapping entries. This seems inefficient to me, 
            // as an entry is only worth searching if it contains (NOT overlaps)
            // the rectangle we are searching for.
            //
            // Also the algorithm has been changed so that it is not recursive.

            // FL1 [Search subtrees] If root is not a leaf, check each entry 
            // to determine if it contains r. For each entry found, invoke
            // findLeaf on the Node&lt;T&gt; pointed to by the entry, until r is found or
            // all entries have been checked.
            parents.Clear();
            parents.Push(rootNodeId);

            parentsEntry.Clear();
            parentsEntry.Push(-1);
            Node<T> n = null;
            var foundIndex = -1;  // index of entry to be deleted in leaf

            while (foundIndex == -1 && parents.Count > 0)
            {
                n = getNode(parents.Peek());
                var startIndex = parentsEntry.Peek() + 1;

                if (!n.isLeaf())
                {
                    var contains = false;
                    for (var i = startIndex; i < n.entryCount; i++)
                    {
                        if (n.entries[i].contains(r))
                        {
                            parents.Push(n.ids[i]);
                            parentsEntry.Pop();
                            parentsEntry.Push(i); // this becomes the start index when the child has been searched
                            parentsEntry.Push(-1);
                            contains = true;
                            break; // ie go to next iteration of while()
                        }
                    }
                    if (contains)
                    {
                        continue;
                    }
                }
                else
                {
                    foundIndex = n.findEntry(r, id);
                }

                parents.Pop();
                parentsEntry.Pop();
            } // while not found

            if (foundIndex != -1)
            {
                n.deleteEntry(foundIndex, minNodeEntries);
                condenseTree(n);
                msize--;
            }

            // shrink the tree if possible (i.e. if root Node&lt;T%gt; has exactly one entry,and that 
            // entry is not a leaf node, delete the root (it's entry becomes the new root)
            var root = getNode(rootNodeId);
            while (root.entryCount == 1 && treeHeight > 1)
            {
                root.entryCount = 0;
                rootNodeId = root.ids[0];
                treeHeight--;
                root = getNode(rootNodeId);
            }

            return (foundIndex != -1);
        }

        /// <summary>
        /// Retrieve nearest items to a point in radius furthestDistance
        /// </summary>
        /// <param name="p">Point of origin</param>
        /// <param name="furthestDistance">maximum distance</param>
        /// <returns>List of items</returns>
        public List<T> Nearest(Point p, float furthestDistance)
        {
            var retval = new List<T>();
            nearest(p, delegate(int id)
            {
                retval.Add(IdsToItems[id]);
            }, furthestDistance);
            return retval;
        }
        void nearest(Point p, Action<int> v, float furthestDistance)
        {
            var rootNode = getNode(rootNodeId);

            nearest(p, rootNode, furthestDistance);

            foreach (var id in nearestIds)
                v(id);
            nearestIds.Clear();
        }

        /// <summary>
        /// Retrieve items which intersect with Rectangle r
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
        public List<T> Intersects(Rectangle r)
        {
            var retval = new List<T>();
            intersects(r, id => retval.Add(IdsToItems[id]));
            return retval;
        }
        void intersects(Rectangle r, Action<int> v)
        {
            var rootNode = getNode(rootNodeId);
            intersects(r, v, rootNode);
        }

        /// <summary>
        /// find all rectangles in the tree that are contained by the passed rectangle
        /// written to be non-recursive (should model other searches on this?)</summary>
        /// <param name="r"></param>
        /// <returns></returns>
        public List<T> Contains(Rectangle r)
        {
            var retval = new List<T>();
            contains(r, id => retval.Add(IdsToItems[id]));

            return retval;
        }
        void contains(Rectangle r, Action<int> v)
        {
            var _parents = new Stack<int>();
            //private TIntStack parentsEntry = new TIntStack();
            var _parentsEntry = new Stack<int>();


            // find all rectangles in the tree that are contained by the passed rectangle
            // written to be non-recursive (should model other searches on this?)

            _parents.Clear();
            _parents.Push(rootNodeId);

            _parentsEntry.Clear();
            _parentsEntry.Push(-1);

            // TODO: possible shortcut here - could test for intersection with the 
            // MBR of the root node. If no intersection, return immediately.

            while (_parents.Count > 0)
            {
                var n = getNode(_parents.Peek());
                var startIndex = _parentsEntry.Peek() + 1;

                if (!n.isLeaf())
                {
                    // go through every entry in the index Node<T> to check
                    // if it intersects the passed rectangle. If so, it 
                    // could contain entries that are contained.
                    var intersects = false;
                    for (var i = startIndex; i < n.entryCount; i++)
                    {
                        if (r.intersects(n.entries[i]))
                        {
                            _parents.Push(n.ids[i]);
                            _parentsEntry.Pop();
                            _parentsEntry.Push(i); // this becomes the start index when the child has been searched
                            _parentsEntry.Push(-1);
                            intersects = true;
                            break; // ie go to next iteration of while()
                        }
                    }
                    if (intersects)
                    {
                        continue;
                    }
                }
                else
                {
                    // go through every entry in the leaf to check if 
                    // it is contained by the passed rectangle
                    for (var i = 0; i < n.entryCount; i++)
                    {
                        if (r.contains(n.entries[i]))
                        {
                            v(n.ids[i]);
                        }
                    }
                }
                _parents.Pop();
                _parentsEntry.Pop();
            }
        }

        /// <summary>
        /// Returns the bounds of all the entries in the spatial index, or null if there are no entries.
        /// </summary>
        public Rectangle getBounds()
        {
            Rectangle bounds = null;

            var n = getNode(getRootNodeId());
            if (n != null && n.getMBR() != null)
            {
                bounds = n.getMBR().copy();
            }
            return bounds;
        }

        /// <summary>
        /// Returns a string identifying the type of spatial index, and the version number
        /// </summary>
        public string getVersion()
        {
            return "RTree-" + version;
        }
        //-------------------------------------------------------------------------
        // end of SpatialIndex methods
        //-------------------------------------------------------------------------


        /// <summary>
        /// Get the next available Node&lt;T&gt; ID. Reuse deleted Node&lt;T&gt; IDs if
        /// possible
        /// </summary>
        int getNextNodeId()
        {
            var nextNodeId = 0;
            if (deletedNodeIds.Count > 0)
            {
                nextNodeId = deletedNodeIds.Pop();
            }
            else
            {
                nextNodeId = 1 + highestUsedNodeId++;
            }
            return nextNodeId;
        }

       
       
       

        /// <summary>
        /// Get a Node&lt;T&gt; object, given the ID of the node.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        Node<T> getNode(int index) => nodeMap[index];

        public int getRootNodeId() => rootNodeId;
        /// <summary>
        /// Split a node. Algorithm is taken pretty much verbatim from
        /// Guttman's original paper.
        /// </summary>
        /// <param name="n"></param>
        /// <param name="newRect"></param>
        /// <param name="newId"></param>
        /// <returns>return new Node&lt;T&gt; object.</returns>
        Node<T> splitNode(Node<T> n, Rectangle newRect, int newId)
        {
            // [Pick first entry for each group] Apply algorithm pickSeeds to 
            // choose two entries to be the first elements of the groups. Assign
            // each to a group.

            Array.Copy(initialEntryStatus, 0, entryStatus, 0, maxNodeEntries);

            Node<T> newNode = null;
            newNode = new Node<T>(getNextNodeId(), n.level, maxNodeEntries);
            nodeMap[newNode.nodeId] =  newNode;

            pickSeeds(n, newRect, newId, newNode); // this also sets the entryCount to 1

            // [Check if done] If all entries have been assigned, stop. If one
            // group has so few entries that all the rest must be assigned to it in 
            // order for it to have the minimum number m, assign them and stop. 
            while (n.entryCount + newNode.entryCount < maxNodeEntries + 1)
            {
                if (maxNodeEntries + 1 - newNode.entryCount == minNodeEntries)
                {
                    // assign all remaining entries to original node
                    for (var i = 0; i < maxNodeEntries; i++)
                    {
                        if (entryStatus[i] == ENTRY_STATUS_UNASSIGNED)
                        {
                            entryStatus[i] = ENTRY_STATUS_ASSIGNED;
                            n.mbr.add(n.entries[i]);
                            n.entryCount++;
                        }
                    }
                    break;
                }
                if (maxNodeEntries + 1 - n.entryCount == minNodeEntries)
                {
                    // assign all remaining entries to new node
                    for (var i = 0; i < maxNodeEntries; i++)
                    {
                        if (entryStatus[i] == ENTRY_STATUS_UNASSIGNED)
                        {
                            entryStatus[i] = ENTRY_STATUS_ASSIGNED;
                            newNode.addEntryNoCopy(n.entries[i], n.ids[i]);
                            n.entries[i] = null;
                        }
                    }
                    break;
                }

                // [Select entry to assign] Invoke algorithm pickNext to choose the
                // next entry to assign. Add it to the group whose covering rectangle 
                // will have to be enlarged least to accommodate it. Resolve ties
                // by adding the entry to the group with smaller area, then to the 
                // the one with fewer entries, then to either. Repeat from S2
                pickNext(n, newNode);
            }

            n.reorganize(this);


            return newNode;
        }

        /// <summary>
        /// Pick the seeds used to split a node.
        /// Select two entries to be the first elements of the groups
        /// </summary>
        /// <param name="n"></param>
        /// <param name="newRect"></param>
        /// <param name="newId"></param>
        /// <param name="newNode"></param>
        void pickSeeds(Node<T> n, Rectangle newRect, int newId, Node<T> newNode)
        {
            // Find extreme rectangles along all dimension. Along each dimension,
            // find the entry whose rectangle has the highest low side, and the one 
            // with the lowest high side. Record the separation.
            float maxNormalizedSeparation = 0;
            var highestLowIndex = 0;
            var lowestHighIndex = 0;

            // for the purposes of picking seeds, take the MBR of the Node&lt;T&gt; to include
            // the new rectangle aswell.
            n.mbr.add(newRect);

            for (var d = 0; d < Rectangle.DIMENSIONS; d++)
            {
                var tempHighestLow = newRect.min[d];
                var tempHighestLowIndex = -1; // -1 indicates the new rectangle is the seed

                var tempLowestHigh = newRect.max[d];
                var tempLowestHighIndex = -1;

                for (var i = 0; i < n.entryCount; i++)
                {
                    var tempLow = n.entries[i].min[d];
                    if (tempLow >= tempHighestLow)
                    {
                        tempHighestLow = tempLow;
                        tempHighestLowIndex = i;
                    }
                    else
                    {  // ensure that the same index cannot be both lowestHigh and highestLow
                        var tempHigh = n.entries[i].max[d];
                        if (tempHigh <= tempLowestHigh)
                        {
                            tempLowestHigh = tempHigh;
                            tempLowestHighIndex = i;
                        }
                    }

                    // PS2 [Adjust for shape of the rectangle cluster] Normalize the separations
                    // by dividing by the widths of the entire set along the corresponding
                    // dimension
                    var normalizedSeparation = (tempHighestLow - tempLowestHigh) / (n.mbr.max[d] - n.mbr.min[d]);

                    // PS3 [Select the most extreme pair] Choose the pair with the greatest
                    // normalized separation along any dimension.
                    if (normalizedSeparation > maxNormalizedSeparation)
                    {
                        maxNormalizedSeparation = normalizedSeparation;
                        highestLowIndex = tempHighestLowIndex;
                        lowestHighIndex = tempLowestHighIndex;
                    }
                }
            }

            // highestLowIndex is the seed for the new node.
            if (highestLowIndex == -1)
            {
                newNode.addEntry(newRect, newId);
            }
            else
            {
                newNode.addEntryNoCopy(n.entries[highestLowIndex], n.ids[highestLowIndex]);
                n.entries[highestLowIndex] = null;

                // move the new rectangle into the space vacated by the seed for the new node
                n.entries[highestLowIndex] = newRect;
                n.ids[highestLowIndex] = newId;
            }

            // lowestHighIndex is the seed for the original node. 
            if (lowestHighIndex == -1)
            {
                lowestHighIndex = highestLowIndex;
            }

            entryStatus[lowestHighIndex] = ENTRY_STATUS_ASSIGNED;
            n.entryCount = 1;
            n.mbr.set(n.entries[lowestHighIndex].min, n.entries[lowestHighIndex].max);
        }



        
        /// <summary>
        /// Pick the next entry to be assigned to a group during a Node&lt;T&gt; split.
        /// [Determine cost of putting each entry in each group] For each 
        /// entry not yet in a group, calculate the area increase required
        /// in the covering rectangles of each group  
        /// </summary>
        /// <param name="n"></param>
        /// <param name="newNode"></param>
        /// <returns></returns>
        int pickNext(Node<T> n, Node<T> newNode)
        {
            var maxDifference = float.NegativeInfinity;
            var next = 0;
            var nextGroup = 0;

            maxDifference = float.NegativeInfinity;


            for (var i = 0; i < maxNodeEntries; i++)
            {
                if (entryStatus[i] == ENTRY_STATUS_UNASSIGNED)
                {
                    var nIncrease = n.mbr.enlargement(n.entries[i]);
                    var newNodeIncrease = newNode.mbr.enlargement(n.entries[i]);
                    var difference = Math.Abs(nIncrease - newNodeIncrease);

                    if (difference > maxDifference)
                    {
                        next = i;

                        if (nIncrease < newNodeIncrease)
                        {
                            nextGroup = 0;
                        }
                        else if (newNodeIncrease < nIncrease)
                        {
                            nextGroup = 1;
                        }
                        else if (n.mbr.area() < newNode.mbr.area())
                        {
                            nextGroup = 0;
                        }
                        else if (newNode.mbr.area() < n.mbr.area())
                        {
                            nextGroup = 1;
                        }
                        else if (newNode.entryCount < maxNodeEntries / 2)
                        {
                            nextGroup = 0;
                        }
                        else
                        {
                            nextGroup = 1;
                        }
                        maxDifference = difference;
                    }
                }
            }

            entryStatus[next] = ENTRY_STATUS_ASSIGNED;

            if (nextGroup == 0)
            {
                n.mbr.add(n.entries[next]);
                n.entryCount++;
            }
            else
            {
                // move to new node.
                newNode.addEntryNoCopy(n.entries[next], n.ids[next]);
                n.entries[next] = null;
            }

            return next;
        }

        
        /// <summary>
        /// Recursively searches the tree for the nearest entry. Other queries
        /// call execute() on an IntProcedure when a matching entry is found; 
        /// however nearest() must store the entry Ids as it searches the tree,
        /// in case a nearer entry is found.
        /// Uses the member variable nearestIds to store the nearest
        /// entry IDs.
        /// </summary>
        /// <remarks>TODO rewrite this to be non-recursive?</remarks>
        /// <param name="p"></param>
        /// <param name="n"></param>
        /// <param name="nearestDistance"></param>
        /// <returns></returns>
        float nearest(Point p, Node<T> n, float nearestDistance)
        {
            for (var i = 0; i < n.entryCount; i++)
            {
                var tempDistance = n.entries[i].distance(p);
                if (n.isLeaf())
                { // for leaves, the distance is an actual nearest distance 
                    if (tempDistance < nearestDistance)
                    {
                        nearestDistance = tempDistance;
                        nearestIds.Clear();
                    }
                    if (tempDistance <= nearestDistance)
                    {
                        nearestIds.Add(n.ids[i]);
                    }
                }
                else
                { // for index nodes, only go into them if they potentially could have
                    // a rectangle nearer than actualNearest
                    if (tempDistance <= nearestDistance)
                    {
                        // search the child node
                        nearestDistance = nearest(p, getNode(n.ids[i]), nearestDistance);
                    }
                }
            }
            return nearestDistance;
        }

    
        /// <summary>
        /// Recursively searches the tree for all intersecting entries.
        /// Immediately calls execute() on the passed IntProcedure when 
        /// a matching entry is found.
        /// [x] TODO rewrite this to be non-recursive? Make sure it
        /// doesn't slow it down.
        /// </summary>
        /// <param name="r"></param>
        /// <param name="v"></param>
        /// <param name="n"></param>
        void intersects(Rectangle r, Action<int> v, Node<T> n)
        {
            for (var i = 0; i < n.entryCount; i++)
            {
                if (r.intersects(n.entries[i]))
                {
                    if (n.isLeaf())
                    {
                        v(n.ids[i]);
                    }
                    else
                    {
                        var childNode = getNode(n.ids[i]);
                        intersects(r, v, childNode);
                    }
                }
            }
        }
        Rectangle oldRectangle = new Rectangle(0, 0, 0, 0);

        /// <summary>
        /// Used by delete(). Ensures that all nodes from the passed node
        /// up to the root have the minimum number of entries.
        /// <para>
        /// Note that the parent and parentEntry stacks are expected to
        /// contain the nodeIds of all parents up to the root.
        /// </para>
        /// </summary>
        void condenseTree(Node<T> l)
        {
            // CT1 [Initialize] Set n=l. Set the list of eliminated
            // nodes to be empty.
            var n = l;
            Node<T> parent = null;
            var parentEntry = 0;

            //TIntStack eliminatedNodeIds = new TIntStack();
            var eliminatedNodeIds = new Stack<int>();

            // CT2 [Find parent entry] If N is the root, go to CT6. Otherwise 
            // let P be the parent of N, and let En be N's entry in P  
            while (n.level != treeHeight)
            {
                parent = getNode(parents.Pop());
                parentEntry = parentsEntry.Pop();

                // CT3 [Eliminiate under-full node] If N has too few entries,
                // delete En from P and add N to the list of eliminated nodes
                if (n.entryCount < minNodeEntries)
                {
                    parent.deleteEntry(parentEntry, minNodeEntries);
                    eliminatedNodeIds.Push(n.nodeId);
                }
                else
                {
                    // CT4 [Adjust covering rectangle] If N has not been eliminated,
                    // adjust EnI to tightly contain all entries in N
                    if (!n.mbr.Equals(parent.entries[parentEntry]))
                    {
                        oldRectangle.set(parent.entries[parentEntry].min, parent.entries[parentEntry].max);
                        parent.entries[parentEntry].set(n.mbr.min, n.mbr.max);
                        parent.recalculateMBR(oldRectangle);
                    }
                }
                // CT5 [Move up one level in tree] Set N=P and repeat from CT2
                n = parent;
            }

            // CT6 [Reinsert orphaned entries] Reinsert all entries of nodes in set Q.
            // Entries from eliminated leaf nodes are reinserted in tree leaves as in 
            // Insert(), but entries from higher level nodes must be placed higher in 
            // the tree, so that leaves of their dependent subtrees will be on the same
            // level as leaves of the main tree
            while (eliminatedNodeIds.Count > 0)
            {
                var e = getNode(eliminatedNodeIds.Pop());
                for (var j = 0; j < e.entryCount; j++)
                {
                    add(e.entries[j], e.ids[j], e.level);
                    e.entries[j] = null;
                }
                e.entryCount = 0;
                deletedNodeIds.Push(e.nodeId);
            }
        }
       
        /// <summary>
        /// Used by add(). Chooses a leaf to add the rectangle to.
        /// </summary>
        Node<T> chooseNode(Rectangle r, int level)
        {
            // CL1 [Initialize] Set N to be the root node
            var n = getNode(rootNodeId);
            parents.Clear();
            parentsEntry.Clear();

            // CL2 [Leaf check] If N is a leaf, return N
            while (true)
            {
                if (n.level == level)
                {
                    return n;
                }

                // CL3 [Choose subtree] If N is not at the desired level, let F be the entry in N 
                // whose rectangle FI needs least enlargement to include EI. Resolve
                // ties by choosing the entry with the rectangle of smaller area.
                var leastEnlargement = n.getEntry(0).enlargement(r);
                var index = 0; // index of rectangle in subtree
                for (var i = 1; i < n.entryCount; i++)
                {
                    var tempRectangle = n.getEntry(i);
                    var tempEnlargement = tempRectangle.enlargement(r);
                    if ((tempEnlargement < leastEnlargement) ||
                        ((tempEnlargement == leastEnlargement) &&
                         (tempRectangle.area() < n.getEntry(index).area())))
                    {
                        index = i;
                        leastEnlargement = tempEnlargement;
                    }
                }

                parents.Push(n.nodeId);
                parentsEntry.Push(index);

                // CL4 [Descend until a leaf is reached] Set N to be the child Node&lt;T&gt; 
                // pointed to by Fp and repeat from CL2
                n = getNode(n.ids[index]);
            }
        }

        /// <summary>
        /// Ascend from a leaf Node&lt;T&gt; L to the root, adjusting covering rectangles and
        /// propagating Node&lt;T&gt; splits as necessary.
        /// </summary>
        Node<T> adjustTree(Node<T> n, Node<T> nn)
        {
            // AT1 [Initialize] Set N=L. If L was split previously, set NN to be 
            // the resulting second node.

            // AT2 [Check if done] If N is the root, stop
            while (n.level != treeHeight)
            {

                // AT3 [Adjust covering rectangle in parent entry] Let P be the parent 
                // Node<T> of N, and let En be N's entry in P. Adjust EnI so that it tightly
                // encloses all entry rectangles in N.
                var parent = getNode(parents.Pop());
                var entry = parentsEntry.Pop();

                if (!parent.entries[entry].Equals(n.mbr))
                {
                    parent.entries[entry].set(n.mbr.min, n.mbr.max);
                    parent.mbr.set(parent.entries[0].min, parent.entries[0].max);
                    for (var i = 1; i < parent.entryCount; i++)
                    {
                        parent.mbr.add(parent.entries[i]);
                    }
                }

                // AT4 [Propagate Node<T> split upward] If N has a partner NN resulting from 
                // an earlier split, create a new entry Enn with Ennp pointing to NN and 
                // Enni enclosing all rectangles in NN. Add Enn to P if there is room. 
                // Otherwise, invoke splitNode to produce P and PP containing Enn and
                // all P's old entries.
                Node<T> newNode = null;
                if (nn != null)
                {
                    if (parent.entryCount < maxNodeEntries)
                    {
                        parent.addEntry(nn.mbr, nn.nodeId);
                    }
                    else
                    {
                        newNode = splitNode(parent, nn.mbr.copy(), nn.nodeId);
                    }
                }

                // AT5 [Move up to next level] Set N = P and set NN = PP if a split 
                // occurred. Repeat from AT2
                n = parent;
                nn = newNode;
            }

            return nn;
        }

        /// <summary>
        /// Given a Node<T> object, calculate the Node<T> MBR from it's entries.
        /// Used in consistency checking
        /// </summary>

        public int Count => msize;
    }
}