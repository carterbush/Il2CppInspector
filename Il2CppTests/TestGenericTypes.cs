﻿/*
    Copyright 2019-2020 Katy Coe - http://www.hearthcode.org - http://www.djkaty.com

    All rights reserved.
*/

using System;
using System.IO;
using System.Linq;
using Il2CppInspector.Reflection;
using NUnit.Framework;

namespace Il2CppInspector
{
    [TestFixture]
    public partial class FixedTests 
    {
        // Check generic flags according to https://docs.microsoft.com/en-us/dotnet/api/system.type.isgenerictype?view=netframework-4.8
        [Test]
        public void TestGenericTypes() {

            // Arrange
            // We're currently in IlCppTests\bin\Debug\netcoreapp3.0 or similar
            var testPath = Path.GetFullPath(Directory.GetCurrentDirectory() + @"\..\..\..\TestBinaries\GenericTypes");

            // Build model
            var inspectors = Il2CppInspector.LoadFromFile(testPath + @"\GenericTypes.so", testPath + @"\global-metadata.dat");
            var model = new Il2CppModel(inspectors[0]);

            var asm = model.GetAssembly("GenericTypes.dll");

            // Act
            TypeInfo tBase = asm.GetType("Il2CppTests.TestSources.Base`2");
            TypeInfo tDerived = asm.GetType("Il2CppTests.TestSources.Derived`1");
            TypeInfo tDerivedBase = tDerived.BaseType;
            // TODO: Use a model GetType() once implemented
            TypeInfo tDerivedArray = model.Types.First(t => t.Namespace == "Il2CppTests.TestSources" && t.Name == "Derived`1[System.Int32][]");

            TypeInfo tT = tBase.GenericTypeParameters[0];
            TypeInfo tU = tBase.GenericTypeParameters[1];
            TypeInfo tF = tDerived.GetField("F").FieldType;
            TypeInfo tNested = asm.GetType("Il2CppTests.TestSources.Derived`1+Nested");

            DisplayGenericType(tBase, "Generic type definition Base<T, U>");
            DisplayGenericType(tDerived, "Derived<V>");
            DisplayGenericType(tDerivedBase, "Base type of Derived<V>");
            DisplayGenericType(tDerivedArray, "Array of Derived<int>");
            DisplayGenericType(tT, "Type parameter T from Base<T,U>");
            DisplayGenericType(tU, "Type parameter U from Base<T,U>");
            DisplayGenericType(tF, "Field type, G<Derived<V>>");
            DisplayGenericType(tNested, "Nested type in Derived<V>");

            // Assert
            var checks = new[] {
                (tBase, "Base`2[T,U]", true, true, true, false, -1),
                (tDerived, "Derived`1[V]", true, true, true, false, -1),
                (tDerivedBase, "Base`2[System.String,V]", true, false, true, false, -1),
                (tDerivedArray, "Derived`1[System.Int32][]", false, false, false, false, -1),
                (tT, "T", false, false, true, true, 0),
                (tU, "U", false, false, true, true, 1),
                (tF, "G`1[Derived`1[V]]", true, false, true, false, -1),
                (tNested, "Derived`1[V]+Nested[V]", true, true, true, false, -1)
            };

            foreach (var check in checks) {
                var t = check.Item1;

                Assert.That(t.ToString(), Is.EqualTo(check.Item2));
                Assert.That(t.IsGenericType, Is.EqualTo(check.Item3));
                Assert.That(t.IsGenericTypeDefinition, Is.EqualTo(check.Item4));
                Assert.That(t.ContainsGenericParameters, Is.EqualTo(check.Item5));
                Assert.That(t.IsGenericParameter, Is.EqualTo(check.Item6));
            }
        }

        private void DisplayGenericType(TypeInfo t, string caption) {
            Console.WriteLine("\n{0}", caption);
            Console.WriteLine("    Type: {0}", t);

            Console.WriteLine("\t            IsGenericType: {0}", t.IsGenericType);
            Console.WriteLine("\t  IsGenericTypeDefinition: {0}", t.IsGenericTypeDefinition);
            Console.WriteLine("\tContainsGenericParameters: {0}", t.ContainsGenericParameters);
            Console.WriteLine("\t       IsGenericParameter: {0}", t.IsGenericParameter);

            if (t.IsGenericParameter)
                Console.WriteLine("\t GenericParameterPosition: {0}", t.GenericParameterPosition);
        }
    }
}
