using System;
using System.Reflection.Emit;

namespace Emit.InterfaceProxy
{
    public static class BuilderExtension
    {
        public static void Load(this LocalBuilder @this, ILGenerator il)
        {
            if (@this == null)
            {
                return;
            }
            il.Emit(OpCodes.Ldloc_S, @this.LocalIndex);
        }

        public static void Save(this LocalBuilder @this, ILGenerator il)
        {
            if (@this == null)
            {
                return;
            }
            il.Emit(OpCodes.Stloc_S, @this.LocalIndex);
        }

        public static void DeclareArray<T>(this LocalBuilder @this, ILGenerator il, int length)
        {
            ValidateLocal(@this);
            il.Emit(OpCodes.Ldc_I4_S, length); // declare array length
            il.Emit(OpCodes.Newarr, typeof(T));

            il.Emit(OpCodes.Stloc_S, @this.LocalIndex); // shorter than '@this.Save(il);'
        }

        private static void ValidateLocal(LocalBuilder @this)
        {
            if (@this == null)
            {
                throw new ArgumentNullException("this", "@this can not be null, local variable must be declared.");
            }
        }

        public static void LoadDefault(this LocalBuilder @this, ILGenerator il)
        {
            if (@this == null)
            {
                return;
            }

            if (@this.LocalType.IsValueType)
            {
                switch (@this.LocalType.Name)
                {
                    case "Int64":
                        il.Emit(OpCodes.Ldc_I8, 0L);
                        break;
                    case "Single":
                        il.Emit(OpCodes.Ldc_R4, (float)0);
                        break;
                    case "Double":
                        il.Emit(OpCodes.Ldc_R8, 0.0);
                        break;
                    default: //int32
                        il.Emit(OpCodes.Ldc_I4, 0);
                        break;
                }
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

    }

}
