namespace PPack
{
    public readonly struct DeliveryYieldPlan
    {
        public DeliveryYieldPlan(float retreatRouteDistance, float lateralOffset,
                                 DeliveryRoadSegment sideSegment = null,
                                 bool sideReverse = false, float sideDistance = 0f)
        {
            RetreatRouteDistance = retreatRouteDistance;
            LateralOffset = lateralOffset;
            SideSegment = sideSegment;
            SideReverse = sideReverse;
            SideDistance = sideDistance;
        }

        public float RetreatRouteDistance { get; }
        public float LateralOffset { get; }
        public DeliveryRoadSegment SideSegment { get; }
        public bool SideReverse { get; }
        public float SideDistance { get; }
        public bool UsesSideRoad => SideSegment != null && SideDistance > 0f;
    }
}

