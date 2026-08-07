using System;
using System.ComponentModel.DataAnnotations;
namespace backend.Models
{
	public class FrontendStyleResponse
	{
		public string key {get;set;}
		public string value {get;set;}
	}

	public class MarqueeResponse
	{
		public int year {get;set;}
		public int month {get;set;}
		public string date {get;set;}
	}
}