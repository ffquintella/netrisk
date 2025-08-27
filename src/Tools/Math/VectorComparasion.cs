using System;

namespace Tools.Math;

public class VectorComparasion
{
    public static double EuclideanDistance(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Arrays must have the same length.");

        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double diff = a[i] - b[i];
            sum += diff * diff;
        }
        return System.Math.Sqrt(sum);
    }

}