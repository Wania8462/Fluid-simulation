using NUnit.Framework;
using SimulationLogic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

public class SpatialPartitioningTests
{
    // private static SpatialPartitioning Create3x3Partitioning()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(30, 30), 10);
    //     Vector2[] positions =
    //     {
    //         new(1, 1),
    //         new(11, 1),
    //         new(21, 1),
    //         new(1, 11),
    //         new(11, 11),
    //         new(21, 11),
    //         new(1, 21),
    //         new(11, 21),
    //         new(21, 21)
    //     };

    //     sp.Init(positions);

    //     return sp;
    // }

    // [Test]
    // public void InitTest_CorrectDimentionsCleanDivision()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);

    //     Assert.AreEqual(2, sp.columns);
    //     Assert.AreEqual(2, sp.columns);
    // }

    // [Test]
    // public void InitTest_CorrectDimentionsUnevenDivision()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(11, 11), 5);

    //     Assert.AreEqual(3, sp.columns);
    //     Assert.AreEqual(3, sp.columns);
    // }

    // [Test]
    // public void InitTest_CorrectDimentionsCleanDivisionRectangle()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 15), 5);

    //     Assert.AreEqual(2, sp.columns);
    //     Assert.AreEqual(3, sp.rows);
    // }

    // [Test]
    // public void InitTest_CorrectDimentionsUnevenDivisionRectangle()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(11, 16), 5);

    //     Assert.AreEqual(3, sp.columns);
    //     Assert.AreEqual(4, sp.rows);
    // }

    // [Test]
    // public void InitTest_PopulatesGridCorrectly()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);
    //     Vector2[] positions = { new(1, 1), new(2, 7), new(6, 6) };
        
    //     sp.Init(positions);
        
    //     Assert.AreEqual(1, sp.grid[0].Count);
    //     Assert.AreEqual(0, sp.grid[0][0]);
    //     Assert.AreEqual(1, sp.grid[sp.columns + 1].Count);
    //     Assert.AreEqual(2, sp.grid[sp.columns + 1][0]);
    // }

    // [Test]
    // public void InitTest_PointOutOfBounds()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);
    //     Vector2[] positions = { new(1, 1), new(2, 7), new(16, 6) };

    //     sp.Init(positions);

    //     Assert.AreEqual(1, sp.grid[3].Count);
    // }
    
    // // LogAssert.Expect(LogType.Error, new Regex("^SP: Index is out of range\\..*"));

    // [Test]
    // public void InitTest_PointsBetweenCells()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);
    //     Vector2[] positions = { new(5, 5), new(5, 7), new(2, 0), new(2, 10), new(10, 10)};

    //     sp.Init(positions);

    //     Assert.AreEqual(1, sp.grid[0].Count);
    //     Assert.AreEqual(1, sp.grid[2].Count);
    //     Assert.AreEqual(3, sp.grid[3].Count);
    // }

    // [Test]
    // public void InitTest_ClearsGridBeforePopulating()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);
    //     Vector2[] positions1 = { new(1, 1), new(2, 2) };
    //     Vector2[] positions2 = { new(1, 1) };
        
    //     sp.Init(positions1);
    //     sp.Init(positions2);
        
    //     Assert.AreEqual(1, sp.grid[0].Count);
    //     Assert.AreEqual(0, sp.grid[0][0]);
    // }

    // [Test]
    // public void InitTest_HandlesEmptyPositionArray()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);
    //     Vector2[] positions = { };
        
    //     sp.Init(positions);
        
    //     foreach (var cell in sp.grid)
    //         Assert.AreEqual(0, cell.Count);
    // }

    // [Test]
    // public void InitTest_MultipleParticlesInSameCell()
    // {
    //     var sp = new SpatialPartitioning(Vector2.zero, new Vector2(10, 10), 5);
    //     Vector2[] positions = { new(1, 1), new(3, 2), new(4, 4) };
        
    //     sp.Init(positions);
        
    //     Assert.AreEqual(3, sp.grid[0].Count);
    //     Assert.Contains(0, sp.grid[0]);
    //     Assert.Contains(1, sp.grid[0]);
    //     Assert.Contains(2, sp.grid[0]);
    // }

    // [Test]
    // public void GetNeighbours_CenterCell_ReturnsAllAdjacentCells()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(11, 11));

    //     CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, result);
    // }

    // [Test]
    // public void GetNeighbours_TopEdgeCell_ReturnsTwoRows()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(11, 1));

    //     CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, result);
    // }

    // [Test]
    // public void GetNeighbours_LeftEdgeCell_ReturnsTwoColumns()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(1, 11));

    //     CollectionAssert.AreEquivalent(new[] { 0, 1, 3, 4, 6, 7 }, result);
    // }

    // [Test]
    // public void GetNeighbours_TopLeftCorner_ReturnsFourCells()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(1, 1));

    //     CollectionAssert.AreEquivalent(new[] { 0, 1, 3, 4 }, result);
    // }

    // [Test]
    // public void GetNeighbours_BottomRightCorner_ReturnsFourCells()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(21, 21));

    //     CollectionAssert.AreEquivalent(new[] { 4, 5, 7, 8 }, result);
    // }

    // [Test]
    // public void GetNeighbours_OnCellBoundary_UsesRightCell()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(10, 1));

    //     CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, result);
    // }

    // [Test]
    // public void GetNeighbours_FarOutsideTopLeft_ReturnsOnlyTopLeftCell()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(-11, -11));

    //     CollectionAssert.AreEquivalent(new[] { 0 }, result);
    // }

    // [Test]
    // public void GetNeighbours_LeftOfGrid_ReturnsLeftColumnNeighbours()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(-11, 11));

    //     CollectionAssert.AreEquivalent(new[] { 0, 3, 6 }, result);
    // }

    // [Test]
    // public void GetNeighbours_FarOutsideBottomRight_ReturnsOnlyBottomRightCell()
    // {
    //     var sp = Create3x3Partitioning();

    //     var result = sp.GetNeighbours(new Vector2(31, 31));

    //     CollectionAssert.AreEquivalent(new[] { 8 }, result);
    // }
}