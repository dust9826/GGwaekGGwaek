namespace PPack
{
    public static class DeliveryTrafficPriority
    {
        /// <summary>음수면 first, 양수면 second가 우선이다.</summary>
        public static int Compare(float firstDistanceToExit, float firstDepth, int firstRequestId,
                                  float secondDistanceToExit, float secondDepth, int secondRequestId)
        {
            int exitComparison = firstDistanceToExit.CompareTo(secondDistanceToExit);
            if (exitComparison != 0) return exitComparison;
            int depthComparison = secondDepth.CompareTo(firstDepth);
            if (depthComparison != 0) return depthComparison;
            return firstRequestId.CompareTo(secondRequestId);
        }
    }
}

