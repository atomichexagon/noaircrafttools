#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using hexagonest.Tools;
using static hexagonest.Tools.Utilities;


namespace hexagonest.Validator
{
	internal enum Status { pass, info, warn, fail, critical }

	internal class ValidatorResult
	{
		public readonly string validator_name;
		public readonly Transform root;
		public Status status { get; private set; } = Status.pass;
		public List<Entry> entries = new List<Entry>();

		private int _num_passing = 0;
		public int num_passing { get => _num_passing + entries.Count(entry => entry.status == Status.info); }
		public int num_entries { get => _num_passing + entries.Count; }
		public int num_warnings { get => entries.Count(entry => entry.status > Status.info); }

		public int num_evaluated = 0;

		public ValidatorResult(string name, Transform _root = null)  { validator_name = name; root = _root; }

		private ValidatorResult new_entry(string component, string field, bool condition, Status _status, string message, UnityEngine.Object context, List<Fix> actions = null)
			=> new_entry(validator_name, component, field, condition, _status, message, context, actions);

		private ValidatorResult new_entry(string name, string component, string field, bool condition, Status _status, string message, UnityEngine.Object context, List<Fix> actions = null)
		{
			num_evaluated += 1;
			if (condition) {
				string path = "";
				if (context != null && root != null && context is GameObject c_go) path = hierarchy_path(c_go.transform, root);
				entries.Add(new Entry(validator_name, component, field, path, _status, message, root, context, actions));
				if (_status > status) status = _status;
			}
			else pass();
			return this;
		}

		public void pass() => _num_passing += 1;

		public ValidatorResult info(string component, string field, bool condition, string message, UnityEngine.Object context = null, List<Fix> actions = null)
			=> new_entry(component, field, condition, Status.info, message, context, actions);

		public ValidatorResult warn(string component, string field, bool condition, string message, UnityEngine.Object context = null, List<Fix> actions = null)
			=> new_entry(component, field, condition, Status.warn, message, context, actions);

		public ValidatorResult fail(string component, string field, bool condition, string message, UnityEngine.Object context = null, List<Fix> actions = null)
			=> new_entry(component, field, condition, Status.fail, message, context, actions);

		// always early return after a critical!
		public ValidatorResult critical(string component, string field, bool condition, string message, UnityEngine.Object context = null, List<Fix> actions = null)
			=> new_entry(component, field, condition, Status.critical, message, context, actions);
	}

	internal class Entry
	{
		public string check_name;
		public Status status;
		public string message;
		public Transform root; // check before using
		public UnityEngine.Object context;
		public string path, component, field;
		public List<Fix> actions;

		public Entry(string _name, string _component, string _field, string _path, Status _status, string _message, Transform _root, UnityEngine.Object _context = null, List<Fix> _actions = null)
		{
			check_name = _name; component = _component; field = _field; path = _path; status = _status; message = _message;
			root = _root; context = _context; actions = _actions;
		}
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public class RequiresAttribute : Attribute
	{
		public Type validator { get; }
		public RequiresAttribute(Type _validator) => validator = _validator;
	}

	internal interface IValidator// : IComparable<IValidator>
	{
		string Name { get; }
		ValidatorResult evaluate(UnityEngine.Object aircraft_definition);
	}


	// only to be used when a fix is really trivial (e.g. setting a field to default)
	internal class Fix
	{
		public string name;
		public bool used = false;
		public Action action;

		public Fix(string _name, Action _action)
		{
			name = _name; action = _action;
		}
	}


	internal interface IRule : IComparable<IRule>
	{
		string rule_name { get; } // optional
	}
}

#endif