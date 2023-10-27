// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{

    public class Entity
    {
        public int id;
        public string name;
        public string tag;
        
        public Entity() 
        {
            id = (int)Rand.Between(0, 99999999);
            name = "Entity";
            tag = "Default";
            
        }
    }
}
