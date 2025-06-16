using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Unity.Mathematics.FixedPoint
{
    /// <summary>
    /// A quaternion type for representing rotations in fixed-point math.
    /// </summary>
    [DebuggerTypeProxy(typeof(DebuggerProxy))]
    [System.Serializable]
    public struct fpquaternion : IEquatable<fpquaternion>, IFormattable
    {
        /// <summary>The x component of the quaternion.</summary>
        public fp x;
        /// <summary>The y component of the quaternion.</summary>
        public fp y;
        /// <summary>The z component of the quaternion.</summary>
        public fp z;
        /// <summary>The w component of the quaternion.</summary>
        public fp w;

        /// <summary>A quaternion representing the identity transform.</summary>
        public static readonly fpquaternion identity = new fpquaternion(fp.zero, fp.zero, fp.zero, fp.one);

        /// <summary>Constructs a quaternion from four fp values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public fpquaternion(fp x, fp y, fp z, fp w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        /// <summary>Constructs a quaternion from fp4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public fpquaternion(fp4 value)
        {
            this.x = value.x;
            this.y = value.y;
            this.z = value.z;
            this.w = value.w;
        }

        /// <summary>Constructs a unit quaternion from a fp3x3 rotation matrix.</summary>
        public fpquaternion(fp3x3 m)
        {
            var trace = m.c0.x + m.c1.y + m.c2.z;
            
            if (trace > fp.zero)
            {
                var s = fpmath.sqrt(trace + fp.one) * (fp)2;
                this.w = s * (fp)0.25m;
                this.x = (m.c1.z - m.c2.y) / s;
                this.y = (m.c2.x - m.c0.z) / s;
                this.z = (m.c0.y - m.c1.x) / s;
            }
            else if (m.c0.x > m.c1.y && m.c0.x > m.c2.z)
            {
                var s = fpmath.sqrt(fp.one + m.c0.x - m.c1.y - m.c2.z) * (fp)2;
                this.w = (m.c1.z - m.c2.y) / s;
                this.x = s * (fp)0.25m;
                this.y = (m.c1.x + m.c0.y) / s;
                this.z = (m.c2.x + m.c0.z) / s;
            }
            else if (m.c1.y > m.c2.z)
            {
                var s = fpmath.sqrt(fp.one + m.c1.y - m.c0.x - m.c2.z) * (fp)2;
                this.w = (m.c2.x - m.c0.z) / s;
                this.x = (m.c1.x + m.c0.y) / s;
                this.y = s * (fp)0.25m;
                this.z = (m.c2.y + m.c1.z) / s;
            }
            else
            {
                var s = fpmath.sqrt(fp.one + m.c2.z - m.c0.x - m.c1.y) * (fp)2;
                this.w = (m.c0.y - m.c1.x) / s;
                this.x = (m.c2.x + m.c0.z) / s;
                this.y = (m.c2.y + m.c1.z) / s;
                this.z = s * (fp)0.25m;
            }
        }

        /// <summary>Returns the conjugate of the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion operator -(fpquaternion q)
        {
            return new fpquaternion(-q.x, -q.y, -q.z, q.w);
        }

        /// <summary>Returns the result of multiplying two quaternions together.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion operator *(fpquaternion lhs, fpquaternion rhs)
        {
            return new fpquaternion(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z
            );
        }

        /// <summary>Returns the result of rotating a vector by a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp3 operator *(fpquaternion q, fp3 v)
        {
            fp3 t = (fp)2 * fpmath.cross(q.xyz, v);
            return v + q.w * t + fpmath.cross(q.xyz, t);
        }

        /// <summary>Returns true if two quaternions are equal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(fpquaternion lhs, fpquaternion rhs)
        {
            return lhs.x == rhs.x && lhs.y == rhs.y && lhs.z == rhs.z && lhs.w == rhs.w;
        }

        /// <summary>Returns true if two quaternions are not equal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(fpquaternion lhs, fpquaternion rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>Returns the xyz components of the quaternion as fp3.</summary>
        public fp3 xyz
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new fp3(x, y, z); }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { x = value.x; y = value.y; z = value.z; }
        }

        /// <summary>Returns true if the quaternion is equal to another quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(fpquaternion other)
        {
            return x == other.x && y == other.y && z == other.z && w == other.w;
        }

        /// <summary>Returns true if the quaternion is equal to an object.</summary>
        public override bool Equals(object obj)
        {
            return obj is fpquaternion && Equals((fpquaternion)obj);
        }

        /// <summary>Returns the hash code for the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return (int)math.hash(new uint4(fpmath.asuint(x), fpmath.asuint(y), fpmath.asuint(z), fpmath.asuint(w)));
        }

        /// <summary>Returns a string representation of the quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return string.Format("fpquaternion({0}, {1}, {2}, {3})", x, y, z, w);
        }

        /// <summary>Returns a string representation of the quaternion using a specified format and culture-specific format information.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return string.Format("fpquaternion({0}, {1}, {2}, {3})", 
                x.ToString(format, formatProvider),
                y.ToString(format, formatProvider),
                z.ToString(format, formatProvider),
                w.ToString(format, formatProvider));
        }

        internal sealed class DebuggerProxy
        {
            public fp x;
            public fp y;
            public fp z;
            public fp w;
            public fp3 euler => fpquaternion.ToEuler(new fpquaternion(x, y, z, w));
            
            public DebuggerProxy(fpquaternion q)
            {
                x = q.x;
                y = q.y;
                z = q.z;
                w = q.w;
            }
        }

        /// <summary>Creates a quaternion from angle-axis representation.</summary>
        public static fpquaternion AngleAxis(fp angle, fp3 axis)
        {
            fpmath.sincos(angle * (fp)0.5m, out fp s, out fp c);
            return new fpquaternion(axis.x * s, axis.y * s, axis.z * s, c);
        }

        /// <summary>Creates a quaternion from Euler angles (in radians).</summary>
        public static fpquaternion Euler(fp x, fp y, fp z)
        {
            fpmath.sincos(x * (fp)0.5m, out fp sx, out fp cx);
            fpmath.sincos(y * (fp)0.5m, out fp sy, out fp cy);
            fpmath.sincos(z * (fp)0.5m, out fp sz, out fp cz);

            return new fpquaternion(
                sx * cy * cz - cx * sy * sz,
                cx * sy * cz + sx * cy * sz,
                cx * cy * sz - sx * sy * cz,
                cx * cy * cz + sx * sy * sz
            );
        }

        /// <summary>Creates a quaternion from Euler angles (in radians).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion Euler(fp3 euler)
        {
            return Euler(euler.x, euler.y, euler.z);
        }

        /// <summary>Returns the arcsine of a fixed-point value.</summary>
        private static fp Asin(fp x)
        {
            if (x < -fp.one || x > fp.one)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            // Use identity: asin(x) = atan(x / sqrt(1 - x^2))
            if (x == fp.one) return fpmath.PI_OVER_2;
            if (x == -fp.one) return -fpmath.PI_OVER_2;
            
            return fpmath.atan(x / fpmath.sqrt(fp.one - x * x));
        }

        /// <summary>Converts a quaternion to Euler angles (in radians).</summary>
        public static fp3 ToEuler(fpquaternion q)
        {
            fp3 euler;
            
            fp sinr_cosp = (fp)2 * (q.w * q.x + q.y * q.z);
            fp cosr_cosp = fp.one - (fp)2 * (q.x * q.x + q.y * q.y);
            euler.x = fpmath.atan2(sinr_cosp, cosr_cosp);

            fp sinp = (fp)2 * (q.w * q.y - q.z * q.x);
            if (fpmath.abs(sinp) >= fp.one)
                euler.y = sinp >= fp.zero ? fpmath.PI_OVER_2 : -fpmath.PI_OVER_2;
            else
                euler.y = Asin(sinp);

            fp siny_cosp = (fp)2 * (q.w * q.z + q.x * q.y);
            fp cosy_cosp = fp.one - (fp)2 * (q.y * q.y + q.z * q.z);
            euler.z = fpmath.atan2(siny_cosp, cosy_cosp);

            return euler;
        }

        /// <summary>Returns a quaternion representing a rotation around the x-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion RotateX(fp angle)
        {
            fpmath.sincos(angle * (fp)0.5m, out fp s, out fp c);
            return new fpquaternion(s, fp.zero, fp.zero, c);
        }

        /// <summary>Returns a quaternion representing a rotation around the y-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion RotateY(fp angle)
        {
            fpmath.sincos(angle * (fp)0.5m, out fp s, out fp c);
            return new fpquaternion(fp.zero, s, fp.zero, c);
        }

        /// <summary>Returns a quaternion representing a rotation around the z-axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion RotateZ(fp angle)
        {
            fpmath.sincos(angle * (fp)0.5m, out fp s, out fp c);
            return new fpquaternion(fp.zero, fp.zero, s, c);
        }

        /// <summary>Returns a quaternion that rotates from one direction to another.</summary>
        public static fpquaternion LookRotation(fp3 forward, fp3 up)
        {
            forward = fpmath.normalize(forward);
            fp3 right = fpmath.normalize(fpmath.cross(up, forward));
            up = fpmath.cross(forward, right);

            fp3x3 m = new fp3x3(right, up, forward);
            return new fpquaternion(m);
        }

        /// <summary>Returns the angle in radians between two quaternions.</summary>
        public static fp Angle(fpquaternion q1, fpquaternion q2)
        {
            fp dot = Dot(q1, q2);
            return fp.Acos(fpmath.min(fpmath.abs(dot), fp.one)) * (fp)2;
        }

        /// <summary>Returns the dot product of two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp Dot(fpquaternion q1, fpquaternion q2)
        {
            return q1.x * q2.x + q1.y * q2.y + q1.z * q2.z + q1.w * q2.w;
        }

        /// <summary>Returns the inverse of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion Inverse(fpquaternion q)
        {
            fp lengthSq = Dot(q, q);
            if (lengthSq > fp.zero)
            {
                fp invLength = fp.one / lengthSq;
                return new fpquaternion(-q.x * invLength, -q.y * invLength, -q.z * invLength, q.w * invLength);
            }
            return identity;
        }

        /// <summary>Returns a normalized version of a quaternion.</summary>
        public static fpquaternion Normalize(fpquaternion q)
        {
            fp lengthSq = Dot(q, q);
            if (lengthSq > fp.zero)
            {
                fp invLength = fpmath.rsqrt(lengthSq);
                return new fpquaternion(q.x * invLength, q.y * invLength, q.z * invLength, q.w * invLength);
            }
            return identity;
        }

        /// <summary>Returns a safe normalized version of a quaternion.</summary>
        public static fpquaternion NormalizeSafe(fpquaternion q, fpquaternion defaultValue = default)
        {
            if (defaultValue.w == fp.zero && defaultValue.x == fp.zero && defaultValue.y == fp.zero && defaultValue.z == fp.zero)
                defaultValue = identity;
                
            fp lengthSq = Dot(q, q);
            if (lengthSq > (fp)0.00000001m)
            {
                fp invLength = fpmath.rsqrt(lengthSq);
                return new fpquaternion(q.x * invLength, q.y * invLength, q.z * invLength, q.w * invLength);
            }
            return defaultValue;
        }

        /// <summary>Linearly interpolates between two quaternions.</summary>
        public static fpquaternion Lerp(fpquaternion q1, fpquaternion q2, fp t)
        {
            fp dot = Dot(q1, q2);
            fpquaternion q2Adjusted = dot >= fp.zero ? q2 : new fpquaternion(-q2.x, -q2.y, -q2.z, -q2.w);
            
            fpquaternion result = new fpquaternion(
                q1.x + (q2Adjusted.x - q1.x) * t,
                q1.y + (q2Adjusted.y - q1.y) * t,
                q1.z + (q2Adjusted.z - q1.z) * t,
                q1.w + (q2Adjusted.w - q1.w) * t
            );
            
            return Normalize(result);
        }

        /// <summary>Spherically interpolates between two quaternions.</summary>
        public static fpquaternion Slerp(fpquaternion q1, fpquaternion q2, fp t)
        {
            fp dot = Dot(q1, q2);
            
            // Ensure shortest path
            if (dot < fp.zero)
            {
                q2 = new fpquaternion(-q2.x, -q2.y, -q2.z, -q2.w);
                dot = -dot;
            }
            
            // Use linear interpolation for very close quaternions
            if (dot > (fp)0.9995m)
            {
                return Lerp(q1, q2, t);
            }
            
            // Clamp dot to valid range for acos
            dot = fpmath.clamp(dot, (fp)(-1), fp.one);
            fp theta = fp.Acos(dot);
            fp sinTheta = fpmath.sin(theta);
            
            if (sinTheta > (fp)0.001m)
            {
                fp a = fpmath.sin((fp.one - t) * theta) / sinTheta;
                fp b = fpmath.sin(t * theta) / sinTheta;
                
                return new fpquaternion(
                    a * q1.x + b * q2.x,
                    a * q1.y + b * q2.y,
                    a * q1.z + b * q2.z,
                    a * q1.w + b * q2.w
                );
            }
            else
            {
                // Fall back to linear interpolation
                return Lerp(q1, q2, t);
            }
        }

        /// <summary>Rotates a quaternion towards another quaternion by a maximum angle.</summary>
        public static fpquaternion RotateTowards(fpquaternion from, fpquaternion to, fp maxDegreesDelta)
        {
            fp angle = Angle(from, to);
            if (angle == fp.zero) return to;
            return Slerp(from, to, fpmath.min(fp.one, maxDegreesDelta / angle));
        }
        

    }

    public static partial class fpmath
    {
        /// <summary>Returns a fpquaternion constructed from four fp values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion quaternion(fp x, fp y, fp z, fp w) { return new fpquaternion(x, y, z, w); }

        /// <summary>Returns a fpquaternion constructed from a fp4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion quaternion(fp4 value) { return new fpquaternion(value); }

        /// <summary>Returns a fpquaternion constructed from a fp3x3 rotation matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion quaternion(fp3x3 m) { return new fpquaternion(m); }

        /// <summary>Returns the conjugate of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion conjugate(fpquaternion q) { return -q; }

        /// <summary>Returns the inverse of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion inverse(fpquaternion q) { return fpquaternion.Inverse(q); }

        /// <summary>Returns the dot product of two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp dot(fpquaternion q1, fpquaternion q2) { return fpquaternion.Dot(q1, q2); }

        /// <summary>Returns a normalized version of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion normalize(fpquaternion q) { return fpquaternion.Normalize(q); }

        /// <summary>Returns a safe normalized version of a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion normalizesafe(fpquaternion q, fpquaternion defaultValue = default) 
        { 
            return fpquaternion.NormalizeSafe(q, defaultValue); 
        }

        /// <summary>Linearly interpolates between two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion lerp(fpquaternion q1, fpquaternion q2, fp t) { return fpquaternion.Lerp(q1, q2, t); }

        /// <summary>Spherically interpolates between two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion slerp(fpquaternion q1, fpquaternion q2, fp t) { return fpquaternion.Slerp(q1, q2, t); }

        /// <summary>Returns the angle in radians between two quaternions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp angle(fpquaternion q1, fpquaternion q2) { return fpquaternion.Angle(q1, q2); }

        /// <summary>Rotates a vector by a quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp3 rotate(fpquaternion q, fp3 v) { return q * v; }

        /// <summary>Rotates a quaternion towards another quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion rotateto(fpquaternion from, fpquaternion to, fp maxDegreesDelta) 
        { 
            return fpquaternion.RotateTowards(from, to, maxDegreesDelta); 
        }
    }
}