namespace backend.Models
{
	public class RoleDropdownResponse
	{
		public string role_id {get;set;}
		public string role_name {get;set;}
        public string revoked {get;set;}
	}

    public class ModuleDropdownResponse
    {
        public string md_id {get;set;}
        public string md_name {get;set;}
    }

    public class ModuleDetailDropdownResponse : ModuleDropdownResponse
    {
        public string act_id {get;set;}
        public string act_name {get;set;}
    }
}