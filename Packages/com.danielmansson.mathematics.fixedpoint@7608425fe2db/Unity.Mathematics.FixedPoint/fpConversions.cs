using System.Runtime.CompilerServices;
using UnityEngine;

namespace Unity.Mathematics.FixedPoint
{
    /// <summary>
    /// Provides conversion methods between fixed-point types and Unity's standard types.
    /// </summary>
    public static class fpConversions
    {
        #region fp ↔ float conversions
        
        /// <summary>Converts a float to fp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp FloatToFp(float value) { return (fp)value; }
        
        /// <summary>Converts a fp to float.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FpToFloat(fp value) { return (float)value; }
        
        #endregion

        #region fp2 ↔ Vector2 conversions
        
        /// <summary>Converts a Vector2 to fp2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp2 Vector2ToFp2(Vector2 vector) 
        { 
            return new fp2((fp)vector.x, (fp)vector.y); 
        }
        
        /// <summary>Converts a fp2 to Vector2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Fp2ToVector2(fp2 value) 
        { 
            return new Vector2((float)value.x, (float)value.y); 
        }
        
        #endregion

        #region fp3 ↔ Vector3 conversions
        
        /// <summary>Converts a Vector3 to fp3.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp3 Vector3ToFp3(Vector3 vector) 
        { 
            return new fp3((fp)vector.x, (fp)vector.y, (fp)vector.z); 
        }
        
        /// <summary>Converts a fp3 to Vector3.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Fp3ToVector3(fp3 value) 
        { 
            return new Vector3((float)value.x, (float)value.y, (float)value.z); 
        }
        
        #endregion

        #region fp4 ↔ Vector4 conversions
        
        /// <summary>Converts a Vector4 to fp4.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp4 Vector4ToFp4(Vector4 vector) 
        { 
            return new fp4((fp)vector.x, (fp)vector.y, (fp)vector.z, (fp)vector.w); 
        }
        
        /// <summary>Converts a fp4 to Vector4.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 Fp4ToVector4(fp4 value) 
        { 
            return new Vector4((float)value.x, (float)value.y, (float)value.z, (float)value.w); 
        }
        
        #endregion

        #region fpquaternion ↔ Quaternion conversions
        
        /// <summary>Converts a Unity Quaternion to fpquaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion QuaternionToFpQuaternion(Quaternion quaternion) 
        { 
            return new fpquaternion((fp)quaternion.x, (fp)quaternion.y, (fp)quaternion.z, (fp)quaternion.w); 
        }
        
        /// <summary>Converts a fpquaternion to Unity Quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion FpQuaternionToQuaternion(fpquaternion value) 
        { 
            return new Quaternion((float)value.x, (float)value.y, (float)value.z, (float)value.w); 
        }
        
        #endregion
    }

    /// <summary>
    /// Extension methods for convenient conversions
    /// </summary>
    public static class fpConversionExtensions
    {
        /// <summary>Extension method to convert float to fp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp ToFp(this float value) { return (fp)value; }
        
        /// <summary>Extension method to convert fp to float.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToFloat(this fp value) { return (float)value; }
        
        /// <summary>Extension method to convert Vector2 to fp2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp2 ToFp2(this Vector2 vector) { return fpConversions.Vector2ToFp2(vector); }
        
        /// <summary>Extension method to convert fp2 to Vector2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToVector2(this fp2 value) { return fpConversions.Fp2ToVector2(value); }
        
        /// <summary>Extension method to convert Vector3 to fp3.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp3 ToFp3(this Vector3 vector) { return fpConversions.Vector3ToFp3(vector); }
        
        /// <summary>Extension method to convert fp3 to Vector3.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToVector3(this fp3 value) { return fpConversions.Fp3ToVector3(value); }
        
        /// <summary>Extension method to convert Vector4 to fp4.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fp4 ToFp4(this Vector4 vector) { return fpConversions.Vector4ToFp4(vector); }
        
        /// <summary>Extension method to convert fp4 to Vector4.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 ToVector4(this fp4 value) { return fpConversions.Fp4ToVector4(value); }
        
        /// <summary>Extension method to convert Unity Quaternion to fpquaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fpquaternion ToFpQuaternion(this Quaternion quaternion) { return fpConversions.QuaternionToFpQuaternion(quaternion); }
        
        /// <summary>Extension method to convert fpquaternion to Unity Quaternion.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion ToQuaternion(this fpquaternion value) { return fpConversions.FpQuaternionToQuaternion(value); }
    }
}