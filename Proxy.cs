using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace Emit.InterfaceProxy
{
    public abstract class Proxy<T> where T : class
    {
        protected virtual bool CanExecute(string name, Type[] types)
        {
            return true;
        }

        private static readonly MethodInfo _getTypeFromHandle;
        private readonly MethodInfo _canExecute;
        protected readonly Type _interfaceType;

        static Proxy()
        {
            _getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle", new Type[] { typeof(RuntimeTypeHandle) });            
        }

        protected Proxy()
        {
            _canExecute = this.GetType().GetMethod("CanExecute", BindingFlags.Instance | BindingFlags.NonPublic);
            _interfaceType = typeof (T);
        }

        protected object GetDefaultReturnValue(string methodName)
        {
            Type returnType = _interfaceType.GetMethod(methodName).ReturnType;
            if (returnType == typeof(void))
            {
                return null;
            }
            return Activator.CreateInstance(returnType);
        }

        public Type Create(string assemblyName, string className)
        {
            Type res = null;

            AssemblyName assembly = new AssemblyName(assemblyName);
            AssemblyBuilder assemblyBuilder = Thread.GetDomain()
                                                    .DefineDynamicAssembly(assembly, AssemblyBuilderAccess.RunAndSave);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(string.Format("{0}.dll", assemblyName));
            TypeBuilder typeBuilder = moduleBuilder.DefineType(string.Format("{0}.{1}", assemblyName, className),
                                                               TypeAttributes.Class | TypeAttributes.Public, this.GetType());
            typeBuilder.AddInterfaceImplementation(_interfaceType);

            foreach (MemberInfo memberInfo in _interfaceType.GetMembers())
            {
                GenerateMember(typeBuilder, memberInfo);
            }

            res = typeBuilder.CreateType();

            string assemblyFileName;
            if (ShouldSaveAssembly(out assemblyFileName))
            {
                assemblyBuilder.Save(assemblyFileName);
            }

            return res;
        }

        protected virtual bool ShouldSaveAssembly(out string assemblyFileName)
        {
            assemblyFileName = null;
            return false;
        }

        private void GenerateMember(TypeBuilder typeBuilder, MemberInfo info)
        {
            switch (info.MemberType)
            {
                case MemberTypes.Event:
                    GenerateEvent(typeBuilder, (EventInfo)info);
                    break;
                case MemberTypes.Method:
                    {
                        MethodInfo methodInfo = (MethodInfo)info;

                        if (methodInfo.IsSpecialName) // can happen?
                        {
                            break;
                        }

                        GenerateMethod(typeBuilder, methodInfo);
                    }
                    break;
                case MemberTypes.Property:
                    GenerateProperty(typeBuilder, (PropertyInfo)info);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void GenerateEvent(TypeBuilder typeBuilder, EventInfo info)
        {
            EventBuilder eventBuilder = typeBuilder.DefineEvent(info.Name, info.Attributes, info.EventHandlerType);

            foreach (CustomAttributeBuilder attributeBuilder in GetCustumeAttributeBuilders(info))
            {
                eventBuilder.SetCustomAttribute(attributeBuilder);
            }

            MethodInfo addEvent = _interfaceType.GetMethod(string.Format("add_{0}", info.Name));
            eventBuilder.SetAddOnMethod(GenerateMethod(typeBuilder, addEvent));
            MethodInfo removeEvent = _interfaceType.GetMethod(string.Format("remove_{0}", info.Name));
            eventBuilder.SetRemoveOnMethod(GenerateMethod(typeBuilder, removeEvent));

        }

        private void GenerateProperty(TypeBuilder typeBuilder, PropertyInfo info)
        {
            PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(info.Name, info.Attributes, info.PropertyType, null);

            foreach (CustomAttributeBuilder attributeBuilder in GetCustumeAttributeBuilders(info))
            {
                propertyBuilder.SetCustomAttribute(attributeBuilder);
            }

            if (info.CanRead)
            {
                MethodInfo methodInfo = _interfaceType.GetMethod(string.Format("get_{0}", info.Name));
                MethodBuilder method = GenerateMethod(typeBuilder, methodInfo);

                propertyBuilder.SetGetMethod(method);
            }

            if (info.CanWrite)
            {
                MethodInfo methodInfo = _interfaceType.GetMethod(string.Format("set_{0}", info.Name));
                MethodBuilder method = GenerateMethod(typeBuilder, methodInfo);

                propertyBuilder.SetSetMethod(method);
            }

        }

        private MethodBuilder GenerateMethod(TypeBuilder typeBuilder, MethodInfo info)
        {
            Type[] paramTypes = info.GetParameters().Select(p => p.ParameterType).ToArray();
            
            #region Meta Data

            MethodBuilder method = typeBuilder.DefineMethod(info.Name,
                                                             info.Attributes ^ MethodAttributes.Abstract,
                                                             info.ReturnType,
                                                             paramTypes);

            foreach (CustomAttributeBuilder attributeBuilder in GetCustumeAttributeBuilders(info))
            {
                method.SetCustomAttribute(attributeBuilder);
            }


            int i = 0;
            // define return type
            method.DefineParameter(i++, info.ReturnParameter.Attributes, null);

            // define all method arguments (starting with 1)
            foreach (ParameterInfo parameter in info.GetParameters())
            {
                method.DefineParameter(i++, parameter.Attributes, parameter.Name);
            }

            #endregion

            ILGenerator il = method.GetILGenerator();

            #region Declare locals

            Label endOfMethod = il.DefineLabel();

            LocalBuilder types = il.DeclareLocal(typeof(Type[]));

            LocalBuilder returnValue = null;
            if (info.ReturnType != typeof(void))
            {
                returnValue = il.DeclareLocal(info.ReturnType);
            }

            #endregion

            il.Emit(OpCodes.Nop);

            #region Prepate types array to pass to "GetInstance" method

            types.DeclareArray<Type>(il, paramTypes.Length);

            // fill the 'types' array
            i = 0;
            foreach (Type type in paramTypes)
            {
                types.Load(il);

                il.Emit(OpCodes.Ldc_I4_S, i++); // set the index to array
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, _getTypeFromHandle);
                il.Emit(OpCodes.Stelem_Ref);
            }

            #endregion

            BuildCanExecute(info, method, types, returnValue, endOfMethod);

            BuildMethodBody(info, method, types, returnValue, endOfMethod);

            il.Emit(OpCodes.Br_S, endOfMethod);

            il.MarkLabel(endOfMethod);

            returnValue.Load(il);

            //return
            il.Emit(OpCodes.Ret);

            return method;

        }


        private IEnumerable<CustomAttributeBuilder> GetCustumeAttributeBuilders(MemberInfo memberInfo)
        {
            foreach (CustomAttributeData attributeData in memberInfo.GetCustomAttributesData())
            {
                yield return GetCustumeAttributeBuilder(attributeData);
            }
            foreach (Attribute attribute in GetAdditionalAttributes(memberInfo))
            {
                yield return GetCustumeAttributeBuilder(attribute);
            }
        }

        protected IEnumerable<Attribute> GetAdditionalAttributes(MemberInfo memberInfo)
        {
            yield break;
        }

        private CustomAttributeBuilder GetCustumeAttributeBuilder(Attribute attribute)
        {
            throw new NotImplementedException("Please do not overide GetAdditionalAttributes(MemberInfo memberInfo) yet.");
        }

        private CustomAttributeBuilder GetCustumeAttributeBuilder(CustomAttributeData attributeData)
        {
            List<object> namedFieldValues = new List<object>();
            List<FieldInfo> fields = new List<FieldInfo>();
            List<object> constructorArguments = new List<object>();

            foreach (CustomAttributeTypedArgument cata in attributeData.ConstructorArguments)
            {
                constructorArguments.Add(cata.Value);
            }

            if (attributeData.NamedArguments.Count > 0)
            {
                FieldInfo[] possibleFields = attributeData.GetType().GetFields();

                foreach (CustomAttributeNamedArgument cana in attributeData.NamedArguments)
                {
                    for (int x = 0; x < possibleFields.Length; x++)
                    {
                        if (possibleFields[x].Name.CompareTo(cana.MemberInfo.Name) == 0)
                        {
                            fields.Add(possibleFields[x]);
                            namedFieldValues.Add(cana.TypedValue.Value);
                        }
                    }
                }
            }

            return new CustomAttributeBuilder(
                attributeData.Constructor, 
                constructorArguments.ToArray(), 
                fields.ToArray(), 
                namedFieldValues.ToArray());
        }

        private void BuildCanExecute(MethodInfo info, MethodBuilder method, LocalBuilder types, LocalBuilder returnValue, Label endOfMethod)
        {

            if (IsCanExecuteAlwaysTrue())
            {
                return;
            }
            ILGenerator il = method.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            LocalBuilder canExecute = il.DeclareLocal(typeof(bool));
            // pass method name to "CanExecute" method
            il.Emit(OpCodes.Ldstr, info.Name);

            // pass types to "CanExecute" method
            types.Load(il);

            il.Emit(OpCodes.Callvirt, _canExecute);

            canExecute.Save(il);

            canExecute.Load(il);

            Label continueToExecute = il.DefineLabel();

            il.Emit(OpCodes.Brtrue_S, continueToExecute);

            // if code gets here, can execute is false

            if (returnValue != null)
            {
                // fill the return value with default value - kind of default(T);
                returnValue.LoadDefault(il);

                returnValue.Save(il);
            }
            else
            {
                il.Emit(OpCodes.Nop);
            }

            // exit method
            il.Emit(OpCodes.Br_S, endOfMethod);

            il.MarkLabel(continueToExecute);
        }

        private bool? _isCanExecuteAlwaysTrue;

        private bool IsCanExecuteAlwaysTrue()
        {
            if (_isCanExecuteAlwaysTrue.HasValue)
            {
                return _isCanExecuteAlwaysTrue.Value;
            }

            bool res = true;
            byte[] returnTrueMethodBody = new byte[]
                {
                    (byte) OpCodes.Ldc_I4_1.Value,// set value to true (1)
                    (byte) OpCodes.Stloc_0.Value,
                    (byte) OpCodes.Br_S.Value,
                    (byte) OpCodes.Ldloc_0.Value,
                    (byte) OpCodes.Ret.Value,
                };

            //byte[] returnFalseMethodBody = new byte[]
            //    {
            //        (byte) OpCodes.Ldc_I4_0.Value,// set value to false (0)
            //        (byte) OpCodes.Stloc_0.Value,
            //        (byte) OpCodes.Br_S.Value,
            //        (byte) OpCodes.Ldloc_0.Value,
            //        (byte) OpCodes.Ret.Value,
            //    };

            byte[] ilAsByteArray = _canExecute.GetMethodBody().GetILAsByteArray();
            int length = returnTrueMethodBody.Length;

            int i = 0;
            foreach (byte command in ilAsByteArray.Where(command => command != 0))
            {
                if (i >= length)
                {
                    res = false;
                    break;
                }
                if (command == returnTrueMethodBody[i++])
                {
                    continue;
                }

                res = false;
                break;
            }
            _isCanExecuteAlwaysTrue = res;
            return res;
        }

        protected abstract void BuildMethodBody(MethodInfo info, MethodBuilder method, LocalBuilder types, LocalBuilder returnValue, Label endOfMethod);

    }

}
