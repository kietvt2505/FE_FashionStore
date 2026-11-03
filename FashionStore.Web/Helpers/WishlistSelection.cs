using System.Collections.Generic;

namespace FashionStore.Web.Helpers
{
    public static class WishlistSelection
    {
        public static void Set(List<string> items,string id,bool selected)
        {
            if(!selected){items.RemoveAll(value=>value==id);return;}
            if(!items.Contains(id))items.Add(id);
        }
    }
}
