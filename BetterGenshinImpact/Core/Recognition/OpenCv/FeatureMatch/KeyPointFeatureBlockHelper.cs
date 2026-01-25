using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

public class KeyPointFeatureBlockHelper
{
    public static KeyPointFeatureBlock[][] SplitFeatures(Size originalImage, int rows, int cols, KeyPoint[] keyPoints, Mat matches)
    {
        var matchesCols = matches.Cols; // SURF 64  SIFT 128
        // Calculate grid size
        int cellWidth = originalImage.Width / cols;
        int cellHeight = originalImage.Height / rows;

        // Initialize arrays to store split features and matches
        var splitKeyPoints = new KeyPointFeatureBlock[rows][];

        // Initialize each row
        for (int i = 0; i < rows; i++)
        {
            splitKeyPoints[i] = new KeyPointFeatureBlock[cols];
        }

        // Split features and matches
        for (int i = 0; i < keyPoints.Length; i++)
        {
            int row = (int)(keyPoints[i].Pt.Y / cellHeight);
            int col = (int)(keyPoints[i].Pt.X / cellWidth);

            // Ensure row and col are within bounds
            row = Math.Min(Math.Max(row, 0), rows - 1);
            col = Math.Min(Math.Max(col, 0), cols - 1);

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (splitKeyPoints[row][col] == null)
            {
                splitKeyPoints[row][col] = new KeyPointFeatureBlock();
            }

            splitKeyPoints[row][col].KeyPointList.Add(keyPoints[i]);
            splitKeyPoints[row][col].KeyPointIndexList.Add(i);
        }

        // 遍历每个特征块，计算描述子
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (splitKeyPoints[i][j] == null)
                {
                    continue;
                }

                var block = splitKeyPoints[i][j];

                var descriptor = new Mat(block.KeyPointIndexList.Count, matchesCols, MatType.CV_32FC1);
                InitBlockMat(block.KeyPointIndexList, descriptor, matches);

                block.Descriptor = descriptor;
            }
        }

        return splitKeyPoints;
    }

    public static (int, int) GetCellIndex(Size originalImage, int rows, int cols, float x, float y)
    {
        // Calculate grid size
        int cellWidth = originalImage.Width / cols;
        int cellHeight = originalImage.Height / rows;

        // Calculate cell index for the given point
        var cellRow = (int)Math.Round(y / cellHeight, 0);
        var cellCol = (int)Math.Round(x / cellWidth, 0);

        return (cellRow, cellCol);
    }

    public static (int RowStart, int RowEnd, int ColStart, int ColEnd) GetCellRange(Size originalImage, int rows, int cols, Rect rect)
    {
        int cellWidth = originalImage.Width / cols;
        int cellHeight = originalImage.Height / rows;

        var rowStart = (int)(rect.Y / cellHeight);
        var rowEnd = (int)((rect.Y + rect.Height) / cellHeight);
        var colStart = (int)(rect.X / cellWidth);
        var colEnd = (int)((rect.X + rect.Width) / cellWidth);

        rowStart = Math.Max(0, Math.Min(rowStart, rows - 1));
        rowEnd = Math.Max(0, Math.Min(rowEnd, rows - 1));
        colStart = Math.Max(0, Math.Min(colStart, cols - 1));
        colEnd = Math.Max(0, Math.Min(colEnd, cols - 1));

        if (rowEnd < rowStart)
        {
            rowEnd = rowStart;
        }

        if (colEnd < colStart)
        {
            colEnd = colStart;
        }

        return (rowStart, rowEnd, colStart, colEnd);
    }

    public static KeyPointFeatureBlock MergeFeaturesInRange(KeyPointFeatureBlock[][] splitKeyPoints, Mat matches, int rowStart, int rowEnd, int colStart, int colEnd)
    {
        var rows = splitKeyPoints.Length;
        if (rows == 0)
        {
            return new KeyPointFeatureBlock();
        }

        var cols = splitKeyPoints[0].Length;

        rowStart = Math.Max(0, Math.Min(rowStart, rows - 1));
        rowEnd = Math.Max(0, Math.Min(rowEnd, rows - 1));
        colStart = Math.Max(0, Math.Min(colStart, cols - 1));
        colEnd = Math.Max(0, Math.Min(colEnd, cols - 1));

        var matchesCols = matches.Cols;
        var neighboringKeyPoints = new List<KeyPoint>();
        var neighboringKeyPointIndices = new List<int>();

        for (int i = rowStart; i <= rowEnd; i++)
        {
            for (int j = colStart; j <= colEnd; j++)
            {
                if (splitKeyPoints[i][j] != null)
                {
                    neighboringKeyPoints.AddRange(splitKeyPoints[i][j].KeyPointList);
                    neighboringKeyPointIndices.AddRange(splitKeyPoints[i][j].KeyPointIndexList);
                }
            }
        }

        var mergedKeyPointBlock = new KeyPointFeatureBlock
        {
            MergedCenterCellCol = (colStart + colEnd) / 2,
            MergedCenterCellRow = (rowStart + rowEnd) / 2,
            KeyPointList = neighboringKeyPoints,
            KeyPointIndexList = neighboringKeyPointIndices
        };

        mergedKeyPointBlock.Descriptor = new Mat(mergedKeyPointBlock.KeyPointIndexList.Count, matchesCols, MatType.CV_32FC1);
        InitBlockMat(mergedKeyPointBlock.KeyPointIndexList, mergedKeyPointBlock.Descriptor, matches);

        return mergedKeyPointBlock;
    }

    public static KeyPointFeatureBlock MergeNeighboringFeatures(KeyPointFeatureBlock[][] splitKeyPoints, Mat matches, int cellRow, int cellCol)
    {
        var matchesCols = matches.Cols; // SURF 64  SIFT 128
        // Initialize lists to store neighboring features and matches
        var neighboringKeyPoints = new List<KeyPoint>();
        var neighboringKeyPointIndices = new List<int>();

        // Loop through 9 neighboring cells
        for (int i = Math.Max(cellRow - 1, 0); i <= Math.Min(cellRow + 1, splitKeyPoints.Length - 1); i++)
        {
            for (int j = Math.Max(cellCol - 1, 0); j <= Math.Min(cellCol + 1, splitKeyPoints[i].Length - 1); j++)
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (splitKeyPoints[i][j] != null)
                {
                    neighboringKeyPoints.AddRange(splitKeyPoints[i][j].KeyPointList);
                    neighboringKeyPointIndices.AddRange(splitKeyPoints[i][j].KeyPointIndexList);
                }
            }
        }

        // Merge neighboring features
        var mergedKeyPointBlock = new KeyPointFeatureBlock
        {
            MergedCenterCellCol = cellCol,
            MergedCenterCellRow = cellRow,
            KeyPointList = neighboringKeyPoints,
            KeyPointIndexList = neighboringKeyPointIndices
        };
        mergedKeyPointBlock.Descriptor = new Mat(mergedKeyPointBlock.KeyPointIndexList.Count, matchesCols, MatType.CV_32FC1);
        InitBlockMat(mergedKeyPointBlock.KeyPointIndexList, mergedKeyPointBlock.Descriptor, matches);

        return mergedKeyPointBlock;
    }

    /// <summary>
    /// 按行拷贝matches到descriptor
    /// </summary>
    /// <param name="keyPointIndexList">记录了哪些行需要拷贝</param>
    /// <param name="descriptor">拷贝结果</param>
    /// <param name="matches">原始数据</param>
    private static unsafe void InitBlockMat(List<int> keyPointIndexList, Mat descriptor, Mat matches)
    {
        Size size = matches.Size();

        Matrix<float> destMatrix = new(descriptor.DataPointer, size.Width, size.Height);
        Matrix<float> sourceMatrix = new(matches.DataPointer, size.Width, size.Height);

        Span<int> keyPointIndexSpan = CollectionsMarshal.AsSpan(keyPointIndexList);
        ref int keyPointIndexRef = ref MemoryMarshal.GetReference(keyPointIndexSpan);
        for (int i = 0; i < keyPointIndexSpan.Length; i++)
        {
            ref int index = ref Unsafe.Add(ref keyPointIndexRef, i);
            sourceMatrix[index].CopyTo(destMatrix[i]);
        }
    }

    private readonly ref struct Matrix<T>
    {
        private readonly ref T reference;
        private readonly int width;
        private readonly int height;

        public unsafe Matrix(void* pointer, int width, int height)
        {
            reference = ref Unsafe.AsRef<T>(pointer);
            this.width = width;
            this.height = height;
        }

        public Span<T> this[int row]
        {
            get
            {
                if (row < 0 || row >= height)
                {
                    throw new IndexOutOfRangeException();
                }

                return MemoryMarshal.CreateSpan(ref Unsafe.Add(ref reference, row * width), width);
            }
        }
    }

    /// <summary>
    /// 按行拷贝matches到descriptor
    /// </summary>
    /// <param name="keyPointIndexList">记录了哪些行需要拷贝</param>
    /// <param name="descriptor">拷贝结果</param>
    /// <param name="matches">原始数据</param>
    [Obsolete]
    private static unsafe void InitBlockMat2(IReadOnlyList<int> keyPointIndexList, Mat descriptor, Mat matches)
    {
        int cols = matches.Cols;
        float* ptrDest = (float*)descriptor.DataPointer;

        for (int i = 0; i < keyPointIndexList.Count; i++)
        {
            var index = keyPointIndexList[i];
            float* ptrSrcRow = (float*)matches.Ptr(index);
            for (int j = 0; j < cols; j++)
            {
                *(ptrDest + i * cols + j) = ptrSrcRow[j];
            }
        }
    }
}
