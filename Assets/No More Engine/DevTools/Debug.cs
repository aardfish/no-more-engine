using UnityEngine;


namespace NoMoreEngine.DevTools
{
    public class Debug : UnityEngine.Debug
    {
        public enum DrawingPlane
        {
            XY,
            XZ,
            YZ
        }

        public static void DrawCircle(Vector3 position, Quaternion orientation, float radius, Color color, int segments = 16)
        {
            if (radius <= 0.0f || segments <= 0)
            {
                return;
            }

            float angleStep = (360.0f / segments);

            angleStep *= Mathf.Deg2Rad;

            Vector3 lineStart = Vector3.zero;
            Vector3 lineEnd = Vector3.zero;

            for (int i = 0; i < segments; i++)
            {
                lineStart.x = Mathf.Cos(angleStep * i);
                lineStart.y = Mathf.Sin(angleStep * i);
                lineStart.z = 0.0f;

                lineEnd.x = Mathf.Cos(angleStep * (i + 1));
                lineEnd.y = Mathf.Sin(angleStep * (i + 1));
                lineEnd.z = 0.0f;

                lineStart *= radius;
                lineEnd *= radius;

                lineStart = orientation * lineStart;
                lineEnd = orientation * lineEnd;

                lineStart += position;
                lineEnd += position;

                DrawLine(lineStart, lineEnd, color);
            }
        }

        public static void DrawCircle(Vector3 position, DrawingPlane drawingPlane, float radius, Color color, int segments = 16)
        {
            switch (drawingPlane)
            {
                case DrawingPlane.XY:
                    DrawCircleXY(position, radius, color, segments);
                    break;

                case DrawingPlane.XZ:
                    DrawCircleXZ(position, radius, color, segments);
                    break;

                case DrawingPlane.YZ:
                    DrawCircleYZ(position, radius, color, segments);
                    break;

                default:
                    DrawCircleXY(position, radius, color, segments);
                    break;
            }
        }

        public static void DrawCircleXY(Vector3 position, float radius, Color color, int segments = 16)
        {
            DrawCircle(position, Quaternion.identity, radius, color, segments);
        }

        public static void DrawCircleXZ(Vector3 position, float radius, Color color, int segments = 16)
        {
            DrawCircle(position, Quaternion.Euler(0, -90, 0), radius, color, segments);
        }

        public static void DrawCircleYZ(Vector3 position, float radius, Color color, int segments = 16)
        {
            DrawCircle(position, Quaternion.Euler(90, 0, 0), radius, color, segments);
        }

        public static void DrawArc(float startAngle, float endAngle,
        Vector3 position, Quaternion orientation, float radius,
        Color color, bool drawChord = false, bool drawSector = false,
        int arcSegments = 32)
        {
            float arcSpan = Mathf.DeltaAngle(startAngle, endAngle);

            if (arcSpan <= 0)
            {
                arcSpan += 360.0f;
            }

            float angleStep = (arcSpan / arcSegments) * Mathf.Deg2Rad;
            float stepOffset = startAngle * Mathf.Deg2Rad;

            float stepStart = 0.0f;
            float stepEnd = 0.0f;
            Vector3 lineStart = Vector3.zero;
            Vector3 lineEnd = Vector3.zero;

            Vector3 arcStart = Vector3.zero;
            Vector3 arcEnd = Vector3.zero;

            Vector3 arcOrigin = position;

            for (int i = 0; i < arcSegments; i++)
            {
                stepStart = angleStep * i + stepOffset;
                stepEnd = angleStep * (i + 1) + stepOffset;

                lineStart.x = Mathf.Cos(stepStart);
                lineStart.y = Mathf.Sin(stepStart);
                lineStart.z = 0.0f;

                lineEnd.x = Mathf.Cos(stepEnd);
                lineEnd.y = Mathf.Sin(stepEnd);
                lineEnd.z = 0.0f;

                lineStart *= radius;
                lineEnd *= radius;

                lineStart = orientation * lineStart;
                lineEnd = orientation * lineEnd;

                lineStart += position;
                lineEnd += position;

                if (i == 0)
                {
                    arcStart = lineStart;
                }

                if (i == arcSegments - 1)
                {
                    arcEnd = lineEnd;
                }

                DrawLine(lineStart, lineEnd, color);
            }

            if (drawChord)
            {
                DrawLine(arcStart, arcEnd, color);
            }
            if (drawSector)
            {
                DrawLine(arcStart, arcOrigin, color);
                DrawLine(arcEnd, arcOrigin, color);
            }
        }

        public static void DrawArc(float startAngle, float endAngle,
        Vector3 position, DrawingPlane drawingPlane, float radius,
        Color color, bool drawChord = false, bool drawSector = false,
        int arcSegments = 16)
        {
            switch (drawingPlane)
            {
                case DrawingPlane.XY:
                    DrawArcXY(startAngle, endAngle, position, radius, color,
                        drawChord, drawSector, arcSegments);
                    break;

                case DrawingPlane.XZ:
                    DrawArcXZ(startAngle, endAngle, position, radius, color,
                        drawChord, drawSector, arcSegments);
                    break;

                case DrawingPlane.YZ:
                    DrawArcYZ(startAngle, endAngle, position, radius, color,
                        drawChord, drawSector, arcSegments);
                    break;

                default:
                    DrawArcXY(startAngle, endAngle, position, radius, color,
                        drawChord, drawSector, arcSegments);
                    break;
            }
        }

        public static void DrawArcXY(float startAngle, float endAngle,
        Vector3 position, float radius, Color color, bool drawChord = false,
        bool drawSector = false, int arcSegments = 16)
        {
            DrawArc(startAngle, endAngle, position, Quaternion.identity, radius,
                color, drawChord, drawSector, arcSegments);
        }

        public static void DrawArcXZ(float startAngle, float endAngle,
            Vector3 position, float radius, Color color, bool drawChord = false,
            bool drawSector = false, int arcSegments = 16)
        {
            DrawArc(startAngle, endAngle, position, Quaternion.Euler(0, -90, 0), radius,
                color, drawChord, drawSector, arcSegments);
        }

        public static void DrawArcYZ(float startAngle, float endAngle,
            Vector3 position, float radius, Color color, bool drawChord = false,
            bool drawSector = false, int arcSegments = 16)
        {
            DrawArc(startAngle, endAngle, position, Quaternion.Euler(90, 0, 0), radius,
                color, drawChord, drawSector, arcSegments);
        }

        public static void DrawTriangle(Vector3 pointA, Vector3 pointB, Vector3 pointC, Color color)
        {
            Debug.DrawLine(pointA, pointB, color);

            Debug.DrawLine(pointB, pointC, color);

            Debug.DrawLine(pointC, pointA, color);
        }

        public static void DrawTriangle(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 offset, Quaternion orientation, Color color)
        {
            pointA = offset + orientation * pointA;
            pointB = offset + orientation * pointB;
            pointC = offset + orientation * pointC;

            DrawTriangle(pointA, pointB, pointC, color);
        }

        public static void DrawTriangle(Vector3 origin, Quaternion orientation, float baseLength, float height, Color color)
        {
            Vector3 pointA = Vector3.right * baseLength * 0.5f;
            Vector3 pointC = Vector3.left * baseLength * 0.5f;
            Vector3 pointB = Vector3.up * height;

            DrawTriangle(pointA, pointB, pointC, origin, orientation, color);
        }

        public static void DrawTriangle(float length, Vector3 center, Quaternion orientation, Color color)
        {
            float radius = length / Mathf.Cos(30.0f * Mathf.Deg2Rad) * 0.5f;
            Vector3 pointA = new Vector3(Mathf.Cos(330.0f * Mathf.Deg2Rad), Mathf.Sin(330.0f * Mathf.Deg2Rad), 0.0f) * radius;
            Vector3 pointB = new Vector3(Mathf.Cos(90.0f * Mathf.Deg2Rad), Mathf.Sin(90.0f * Mathf.Deg2Rad), 0.0f) * radius;
            Vector3 pointC = new Vector3(Mathf.Cos(210.0f * Mathf.Deg2Rad), Mathf.Sin(210.0f * Mathf.Deg2Rad), 0.0f) * radius;

            DrawTriangle(pointA, pointB, pointC, center, orientation, color);
        }

        public static void DrawQuad(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 pointD, Color color)
        {
            DrawLine(pointA, pointB, color);
            DrawLine(pointB, pointC, color);
            DrawLine(pointC, pointD, color);
            DrawLine(pointD, pointA, color);
        }

        public static void DrawRectangle(Vector3 position, Quaternion orientation, Vector2 extent, Color color)
        {
            Vector3 rightOffset = Vector3.right * extent.x * 0.5f;
            Vector3 upOffset = Vector3.up * extent.y * 0.5f;

            Vector3 offsetA = orientation * (rightOffset + upOffset);
            Vector3 offsetB = orientation * (-rightOffset + upOffset);
            Vector3 offsetC = orientation * (-rightOffset - upOffset);
            Vector3 offsetD = orientation * (rightOffset - upOffset);

            DrawQuad(position + offsetA,
                    position + offsetB,
                    position + offsetC,
                    position + offsetD,
                    color);
        }

        public static void DrawRectangle(Vector2 point1, Vector2 point2, Vector3 origin, Quaternion orientation, Color color)
        {
            float extentX = Mathf.Abs(point1.x - point2.x);
            float extentY = Mathf.Abs(point1.y - point2.y);

            Vector3 rotatedRight = orientation * Vector3.right;
            Vector3 rotatedUp = orientation * Vector3.up;

            Vector3 pointA = origin + rotatedRight * point1.x + rotatedUp * point1.y;
            Vector3 pointB = pointA + rotatedRight * extentX;
            Vector3 pointC = pointB + rotatedUp * extentY;
            Vector3 pointD = pointA + rotatedUp * extentY;

            DrawQuad(pointA, pointB, pointC, pointD, color);
        }

        public static void DrawBox(Vector3 position, Quaternion orientation, Vector3 size, Color color)
        {
            Vector3 offsetX = orientation * Vector3.right * size.x * 0.5f;
            Vector3 offsetY = orientation * Vector3.up * size.y * 0.5f;
            Vector3 offsetZ = orientation * Vector3.forward * size.z * 0.5f;

            // Calculate all 8 corners of the box
            Vector3 frontBottomLeft = position - offsetX - offsetY + offsetZ;
            Vector3 frontBottomRight = position + offsetX - offsetY + offsetZ;
            Vector3 frontTopLeft = position - offsetX + offsetY + offsetZ;
            Vector3 frontTopRight = position + offsetX + offsetY + offsetZ;

            Vector3 backBottomLeft = position - offsetX - offsetY - offsetZ;
            Vector3 backBottomRight = position + offsetX - offsetY - offsetZ;
            Vector3 backTopLeft = position - offsetX + offsetY - offsetZ;
            Vector3 backTopRight = position + offsetX + offsetY - offsetZ;

            // Draw front face
            DrawLine(frontBottomLeft, frontBottomRight, color);
            DrawLine(frontBottomRight, frontTopRight, color);
            DrawLine(frontTopRight, frontTopLeft, color);
            DrawLine(frontTopLeft, frontBottomLeft, color);

            // Draw back face
            DrawLine(backBottomLeft, backBottomRight, color);
            DrawLine(backBottomRight, backTopRight, color);
            DrawLine(backTopRight, backTopLeft, color);
            DrawLine(backTopLeft, backBottomLeft, color);

            // Draw connecting edges between front and back faces
            DrawLine(frontBottomLeft, backBottomLeft, color);
            DrawLine(frontBottomRight, backBottomRight, color);
            DrawLine(frontTopLeft, backTopLeft, color);
            DrawLine(frontTopRight, backTopRight, color);
        }

        public static void DrawCube(Vector3 position, Quaternion orientation, float size, Color color)
        {
            DrawBox(position, orientation, Vector3.one * size, color);
        }

        public static void DrawSphere(Vector3 position, Quaternion orientation, float radius, Color color, int segments = 4)
        {
            if (segments < 2)
            {
                segments = 2;
            }

            int doubleSegments = segments * 2;

            float meridianStep = 180.0f / segments;

            for (int i = 0; i < segments; i++)
            {
                DrawCircle(position, orientation * Quaternion.Euler(0, meridianStep * i, 0), radius, color, doubleSegments);
            }

            Vector3 verticalOffset = Vector3.zero;
            float parallelAngleStep = Mathf.PI / segments;
            float stepRadius = 0.0f;
            float stepAngle = 0.0f;

            for (int i = 1; i < segments; i++)
            {
                stepAngle = parallelAngleStep * i;
                verticalOffset = (orientation * Vector3.up) * Mathf.Cos(stepAngle) * radius;
                stepRadius = Mathf.Sin(stepAngle) * radius;

                DrawCircle(position + verticalOffset, orientation * Quaternion.Euler(90.0f, 0, 0), stepRadius, color, doubleSegments);
            }
        }

        public static void DrawCylinder(Vector3 position, Quaternion orientation, float height, float radius, Color color, bool drawFromBase = true)
        {
            Vector3 localUp = orientation * Vector3.up;
            Vector3 localRight = orientation * Vector3.right;
            Vector3 localForward = orientation * Vector3.forward;

            Vector3 basePositionOffset = drawFromBase ? Vector3.zero : (localUp * height * 0.5f);
            Vector3 basePosition = position - basePositionOffset;
            Vector3 topPosition = basePosition + localUp * height;

            Quaternion circleOrientation = orientation * Quaternion.Euler(90, 0, 0);

            Vector3 pointA = basePosition + localRight * radius;
            Vector3 pointB = basePosition + localForward * radius;
            Vector3 pointC = basePosition - localRight * radius;
            Vector3 pointD = basePosition - localForward * radius;

            DrawRay(pointA, localUp * height, color);
            DrawRay(pointB, localUp * height, color);
            DrawRay(pointC, localUp * height, color);
            DrawRay(pointD, localUp * height, color);

            DrawCircle(basePosition, circleOrientation, radius, color, 32);
            DrawCircle(topPosition, circleOrientation, radius, color, 32);
        }

        public static void DrawCapsule(Vector3 position, Quaternion orientation, float height, float radius, Color color, bool drawFromBase = true)
        {
            radius = Mathf.Clamp(radius, 0, height * 0.5f);
            Vector3 localUp = orientation * Vector3.up;
            Quaternion arcOrientation = orientation * Quaternion.Euler(0, 90, 0);

            Vector3 basePositionOffset = drawFromBase ? Vector3.zero : (localUp * height * 0.5f);
            Vector3 baseArcPosition = position + localUp * radius - basePositionOffset;
            DrawArc(180, 360, baseArcPosition, orientation, radius, color);
            DrawArc(180, 360, baseArcPosition, arcOrientation, radius, color);

            float cylinderHeight = height - radius * 2.0f;
            DrawCylinder(baseArcPosition, orientation, cylinderHeight, radius, color, true);

            Vector3 topArcPosition = baseArcPosition + localUp * cylinderHeight;

            DrawArc(0, 180, topArcPosition, orientation, radius, color);
            DrawArc(0, 180, topArcPosition, arcOrientation, radius, color);
        }
    }
}