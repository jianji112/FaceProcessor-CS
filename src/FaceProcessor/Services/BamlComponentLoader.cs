using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xaml;
using System.Xaml.Permissions;

namespace FaceProcessor.Services;

internal static class BamlComponentLoader
{
	public static void Load(object component, string relativePath)
	{
		string fullPath = Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("BAML file was not found.", fullPath);
		}
		Assembly assembly = component.GetType().Assembly;
		Assembly presentationFrameworkAssembly = typeof(Application).Assembly;
		Type settingsType = presentationFrameworkAssembly.GetType("System.Windows.Baml2006.Baml2006ReaderSettings", throwOnError: true)!;
		Type schemaContextType = presentationFrameworkAssembly.GetType("System.Windows.Baml2006.Baml2006SchemaContext", throwOnError: true)!;
		Type readerType = presentationFrameworkAssembly.GetType("System.Windows.Baml2006.Baml2006Reader", throwOnError: true)!;
		object readerSettings = Activator.CreateInstance(settingsType)!;
		settingsType.GetProperty("BaseUri")!.SetValue(readerSettings, new Uri(fullPath, UriKind.Absolute));
		settingsType.GetProperty("LocalAssembly")!.SetValue(readerSettings, assembly);
		XamlObjectWriterSettings writerSettings = new XamlObjectWriterSettings
		{
			AccessLevel = XamlAccessLevel.AssemblyAccessTo(assembly),
			RegisterNamesOnExternalNamescope = true,
			RootObjectInstance = component
		};
		using FileStream stream = File.OpenRead(fullPath);
		object schemaContext = Activator.CreateInstance(schemaContextType, assembly)!;
		ConstructorInfo readerCtor = readerType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Stream), schemaContextType, settingsType, typeof(object) }, null)
			?? throw new MissingMethodException(readerType.FullName, ".ctor(Stream, SchemaContext, ReaderSettings, Object)");
		using XamlReader reader = (XamlReader)readerCtor.Invoke(new[] { stream, schemaContext, readerSettings, component });
		using XamlObjectWriter writer = new XamlObjectWriter(reader.SchemaContext, writerSettings);
		XamlServices.Transform(reader, writer);
	}
}
