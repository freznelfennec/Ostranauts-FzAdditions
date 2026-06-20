using Freznel.FzAdditions.VM.Annotation;
using Freznel.FzAdditions.VM.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Freznel.FzAdditions.VM
{
    public static class VMOperatorUtil
    {
        private static Dictionary<(UnaryOperator, Type), Delegate> UnaryFuncs = new();
        private static Dictionary<(BinaryOperator, Type, Type), Delegate> BinaryFuncs = new();
        private static HashSet<(BinaryOperator, Type, Type)> CommutativeBinaryFuncFlags = new();

        static VMOperatorUtil()
        {
            RegisterAssembly(typeof(VMOperatorUtil).Assembly);
        }

        public static void RegisterAssembly(Assembly assembly)
        {
            var operatorSets = assembly.GetTypes().Where(t => t.IsClass && t.IsDefined(typeof(OperatorSetAttribute), false));

            foreach (var type in operatorSets)
            {
                var methods = type.GetMethods();
                
                foreach (var method in methods)
                {
                    if (!method.IsStatic) continue;
                    if (method.IsDefined(typeof(UnaryOperatorAttribute), false)) RegisterUnaryOperator(method);
                    else if (method.IsDefined(typeof(BinaryOperatorAttribute), false)) RegisterBinaryOperator(method);
                }
            }
        }

        private static void RegisterUnaryOperator(MethodInfo method)
        {
            var methodParams = method.GetParameters();
            if (methodParams.Length != 1) { FzAdditions.Logger.LogError($"Invalid unary operator method {method.DeclaringType.Name}.{method.Name}(): Number of parameters must equal 1"); return; }
            var param1 = methodParams[0];
            if (!param1.ParameterType.IsSubclassOf(typeof(VMObject))) { FzAdditions.Logger.LogError($"Invalid unary operator method {method.DeclaringType.Name}.{method.Name}(): Paramaters types must descend from VMObject"); return; }
            if (method.ReturnType != typeof(VMObject)) { FzAdditions.Logger.LogError($"Invalid unary operator method {method.DeclaringType.Name}.{method.Name}(): Method must return VMObject"); return; }
            var attr = method.GetCustomAttribute<UnaryOperatorAttribute>();
            var key = (attr.Operator, param1.ParameterType);
            var funcType = typeof(Func<,>).MakeGenericType(param1.ParameterType, typeof(VMObject));
            var func = Delegate.CreateDelegate(funcType, method);
            
            if (UnaryFuncs.ContainsKey(key))
            {
                FzAdditions.Logger.LogWarning($"Overriding existing unary operator {key.Operator} {key.ParameterType} with {method.DeclaringType.Name}.{method.Name}()");
                UnaryFuncs[key] = func;
            }
            else
            {
                UnaryFuncs.Add(key, func);
                FzAdditions.Logger.LogInfo($"Registered unary operator {key.Operator} {key.ParameterType} as {method.DeclaringType.Name}.{method.Name}()");
            }
        }

        private static void RegisterBinaryOperator(MethodInfo method)
        {
            var methodParams = method.GetParameters();
            if (methodParams.Length != 2) { FzAdditions.Logger.LogError($"Invalid binary operator method {method.DeclaringType.Name}.{method.Name}(): Number of parameters must equal 2"); return; }
            var param1 = methodParams[0];
            var param2 = methodParams[1];
            if (!param1.ParameterType.IsSubclassOf(typeof(VMObject)) || !param2.ParameterType.IsSubclassOf(typeof(VMObject))) { FzAdditions.Logger.LogError($"Invalid binary operator method {method.DeclaringType.Name}.{method.Name}(): Paramaters types must descend from VMObject"); return; }
            if (method.ReturnType != typeof(VMObject)) { FzAdditions.Logger.LogError($"Invalid binary operator method {method.DeclaringType.Name}.{method.Name}(): Method must return VMObject"); return; }
            var attr = method.GetCustomAttribute<BinaryOperatorAttribute>();
            (BinaryOperator Operator, Type TypeA, Type TypeB) key = (attr.Operator, param1.ParameterType, param2.ParameterType);
            var funcType = typeof(Func<,,>).MakeGenericType(param1.ParameterType, param2.ParameterType, typeof(VMObject));
            var func = Delegate.CreateDelegate(funcType, method);
            
            if (BinaryFuncs.ContainsKey(key))
            {
                FzAdditions.Logger.LogWarning($"Overriding existing binary operator {key.Operator} {key.TypeA} {key.TypeB} with {method.DeclaringType.Name}.{method.Name}()");
                BinaryFuncs[key] = func;
            }
            else
            {
                BinaryFuncs.Add(key, func);
                FzAdditions.Logger.LogInfo($"Registered binary operator {key.Operator} {key.TypeA} {key.TypeB} as {method.DeclaringType.Name}.{method.Name}()");
            }

            if (attr.Commutative && param1.ParameterType != param2.ParameterType) //Add commutative flags if this operation has mismatched commutative types
            {
                if (!CommutativeBinaryFuncFlags.Contains(key)) CommutativeBinaryFuncFlags.Add(key);
                var key2 = (attr.Operator, param2.ParameterType, param1.ParameterType);
                if (!CommutativeBinaryFuncFlags.Contains(key2)) CommutativeBinaryFuncFlags.Add(key2);
            }
        }

        public static VMObject Operate(UnaryOperator @operator, dynamic a)
        {
            Type typeA = a.GetType();
            var key = (@operator, typeA);
            if (!UnaryFuncs.ContainsKey(key)) throw new VMException($"Attempted to perform operation {@operator} on {typeA.Name}");
            return ((dynamic)UnaryFuncs[key])(a);
        }

        public static VMObject Operate(BinaryOperator @operator, dynamic a, dynamic b)
        {
            Type typeA = a.GetType();
            Type typeB = b.GetType();
            var key = (@operator, typeA, typeB);
            if (!BinaryFuncs.ContainsKey(key))
            {
                if (CommutativeBinaryFuncFlags.Contains(key))
                {
                    return ((dynamic)BinaryFuncs[key])(b, a);
                }
                else
                {
                    throw new VMException($"Attempted to perform operation {@operator} on {typeA.Name} and {typeB.Name}");
                }
            }
            return ((dynamic)BinaryFuncs[key])(a, b);
        }


    }
}
