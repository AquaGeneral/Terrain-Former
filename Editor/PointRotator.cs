using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    /// <summary>
    /// A utility struct that rotates a Vecotr2 point around another point and returns it via Rotate().
    /// </summary>
    internal struct PointRotator {
        private float sineOfAngle, cosineOfAngle;
        private Vector2 pivotPoint;

        internal PointRotator(Vector2 pivotPoint) {
            float angleInRadians = TerrainFormerEditor.GetCurrentToolSettings().brushAngle * Mathf.Deg2Rad;
            sineOfAngle = Mathf.Sin(angleInRadians);
            cosineOfAngle = Mathf.Cos(angleInRadians);
            this.pivotPoint = pivotPoint;
        }

        internal PointRotator(float angleInDegrees, Vector2 pivotPoint) {
            float angleInRadians = angleInDegrees * Mathf.Deg2Rad;
            sineOfAngle = Mathf.Sin(angleInRadians);
            cosineOfAngle = Mathf.Cos(angleInRadians);
            this.pivotPoint = pivotPoint;
        }

        internal Vector2 Rotate(Vector2 pointToRotate) {
            if(sineOfAngle == 0f) return pointToRotate;

            pointToRotate -= pivotPoint;

            return new Vector2(
                x: pointToRotate.x * cosineOfAngle - pointToRotate.y * sineOfAngle + pivotPoint.x,
                y: pointToRotate.x * sineOfAngle + pointToRotate.y * cosineOfAngle + pivotPoint.y
            );
        }
    }
}