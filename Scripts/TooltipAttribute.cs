using System;

[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = false)]
public sealed class TooltipAttribute : Attribute 
{
	public TooltipAttribute(string tooltip) { }
}
