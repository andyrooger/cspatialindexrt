﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RTree.Test
{
    /// <summary>
    /// Summary description for UnitTest1
    /// </summary>
    [TestClass]
    public class SimpleTests
    {

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        private RTree<string> Instance { get; set; }

        [TestInitialize]
        public void Setup()
        {
            Instance = new RTree<string>();
            Instance.Add(new Rectangle(0, 0, 0, 0), "Origin");
            Instance.Add(new Rectangle(1, 1, 1, 1), "Box1");

            Instance.Add(new Rectangle(2, 2, 3, 3), "Box 2-3");
        }


        [TestMethod]
        public void TestContainsFound()
        {
            var instancelist = Instance.Contains(new Rectangle(-1, -1, 2, 2));
            Assert.AreEqual(2, instancelist.Count());
        }

        [TestMethod]
        public void TestContainsNotFound()
        {
            var instancelist = Instance.Contains(new Rectangle(5, 5, 6, 6));
            Assert.AreEqual(0, instancelist.Count());
        }

        //[TestMethod]
        //public void TestBounds()
        //{
        //    var bounds = Instance.getBounds();
        //    //X
        //    Assert.AreEqual(0, bounds.get(0).Value.min);
        //    Assert.AreEqual(3, bounds.get(0).Value.max);
        //    //Y
        //    Assert.AreEqual(0, bounds.get(1).Value.min);
        //    Assert.AreEqual(3, bounds.get(1).Value.max);
        //    //Z
        //    Assert.AreEqual(0, bounds.get(2).Value.min);
        //    Assert.AreEqual(3, bounds.get(2).Value.max);
        //}

        [TestMethod]
        public void TestIntersects()
        {
            var intersectlist = Instance.Intersects(new Rectangle(3, 3, 5, 5));
            Assert.AreEqual(1, intersectlist.Count());
            Assert.AreEqual("Box 2-3", intersectlist[0]);
        }

        [TestMethod]
        public void TestNearest()
        {
            var nearestlist = Instance.Nearest(new Point(5, 5), 10);
            Assert.IsTrue(nearestlist.Count() > 0);

            Assert.AreEqual("Box 2-3", nearestlist[0]);
        }

        [TestMethod]
        public void TestDelete()
        {
            var nearestlist = Instance.Nearest(new Point(5, 5), 10);

            Assert.AreEqual("Box 2-3", nearestlist[0]);

            Instance.Delete(new Rectangle(2, 2, 3, 3), "Box 2-3");

            nearestlist = Instance.Nearest(new Point(5, 5), 10);

            Assert.IsTrue(nearestlist.Count() > 0);

            Assert.AreEqual("Box1", nearestlist[0]);
        }

    }
}
