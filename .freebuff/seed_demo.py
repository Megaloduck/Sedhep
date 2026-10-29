import json
import os
import shutil
import sqlite3
import uuid
from datetime import datetime, timedelta, timezone

DEMO = ".freebuff/demo"
os.makedirs(os.path.join(DEMO, "photos"), exist_ok=True)
for name in ("cookbook.db", "cookbook.db-wal", "cookbook.db-shm"):
    path = os.path.join(DEMO, name)
    if os.path.exists(path):
        os.remove(path)

# The live cookbook keeps recent changes in its WAL, so copy through SQLite, not the file system.
source = sqlite3.connect("cookbook.db")
db = sqlite3.connect(os.path.join(DEMO, "cookbook.db"))
source.backup(db)
source.close()
db.execute("PRAGMA foreign_keys=ON")
db.execute("DELETE FROM Ingredients")
db.execute("DELETE FROM RecipeSteps")
db.execute("DELETE FROM RecipePhotos")
db.execute("DELETE FROM Recipes")

categories = {name: cid for cid, name in db.execute("SELECT Id, Name FROM Categories")}

now = datetime.now(timezone.utc)
device = "demo-kitchen"

recipes = [
    {
        "title": "Soto Ayam Lamongan",
        "category": "Soup",
        "description": "The Sunday soup: turmeric broth, shredded chicken and a spoonful of sambal on top.",
        "servings": 6, "prep": 25, "cook": 40, "favorite": 1,
        "cuisine": "Indonesian",
        "notes": "Skim the foam early for a clear broth. The koya goes in at the table, never in the pot.",
        "tags": ["sunday", "mom's"],
        "ingredients": [
            ("1", "kg", "chicken"), ("5", "", "shallots"), ("3", "cloves", "garlic"),
            ("2", "stalks", "lemongrass"), ("1", "tsp", "turmeric"), ("2", "tsp", "salt"),
            ("2", "L", "water"), ("2", "", "lime leaves"), ("1", "handful", "bean sprouts"),
        ],
        "steps": [
            ("Boil the chicken with the lime leaves until tender, then lift it out and shred the meat.", 35),
            ("Grind the shallots, garlic and turmeric, then fry the paste until fragrant.", 8),
            ("Add the paste and lemongrass to the broth and simmer for twenty minutes.", 20),
            ("Serve with the shredded chicken, bean sprouts and a spoon of sambal.", 0),
        ],
        "days": 9,
    },
    {
        "title": "Rendang Daging",
        "category": "Main Dish",
        "description": "Dry-braised beef in coconut milk and spice, cooked slowly until the sauce clings to the meat.",
        "servings": 8, "prep": 30, "cook": 180, "favorite": 1,
        "cuisine": "Indonesian",
        "notes": "Stir every ten minutes near the end, or the bottom catches.",
        "tags": ["festive", "slow"],
        "ingredients": [
            ("1.5", "kg", "beef"), ("800", "ml", "coconut milk"), ("10", "", "shallots"),
            ("6", "cloves", "garlic"), ("8", "", "chili"), ("2", "stalks", "lemongrass"),
            ("2", "tsp", "salt"), ("1", "tbsp", "palm sugar"), ("3", "", "turmeric leaves"),
        ],
        "steps": [
            ("Grind the shallots, garlic and chili into a paste.", 10),
            ("Brown the beef in a heavy pot, then add the paste and lemongrass.", 12),
            ("Pour in the coconut milk and simmer, uncovered, until it thickens.", 90),
            ("Keep going until the sauce darkens and coats the meat, then rest before serving.", 45),
        ],
        "days": 6,
    },
    {
        "title": "Nasi Goreng Kampung",
        "category": "Main Dish",
        "description": "Friday-night fried rice with egg, sweet soy and just enough chili.",
        "servings": 2, "prep": 10, "cook": 12, "favorite": 0,
        "cuisine": "Indonesian",
        "notes": "Cold rice from yesterday fries better than fresh rice.",
        "tags": ["quick", "weeknight"],
        "ingredients": [
            ("2", "cups", "rice"), ("2", "", "egg"), ("3", "", "shallots"),
            ("2", "cloves", "garlic"), ("2", "tbsp", "soy sauce"), ("1", "", "chili"),
            ("1", "tbsp", "oil"), ("1", "", "spring onion"),
        ],
        "steps": [
            ("Fry the shallots, garlic and chili in the oil until soft.", 4),
            ("Push them aside, scramble the eggs, then add the rice.", 4),
            ("Season with soy sauce and toss over high heat until the rice is hot through.", 4),
        ],
        "days": 3,
    },
    {
        "title": "Ayam Bakar Kecap",
        "category": "Main Dish",
        "description": "Grilled chicken glazed with sweet soy and a squeeze of lime.",
        "servings": 4, "prep": 20, "cook": 35, "favorite": 0,
        "cuisine": "Indonesian",
        "notes": "Score the chicken so the glaze reaches the inside.",
        "tags": ["grill", "sunday"],
        "ingredients": [
            ("4", "", "chicken"), ("4", "tbsp", "soy sauce"), ("2", "tbsp", "palm sugar"),
            ("3", "cloves", "garlic"), ("1", "tsp", "salt"), ("1", "", "lime"),
        ],
        "steps": [
            ("Marinate the chicken in garlic, soy sauce and palm sugar for half an hour.", 30),
            ("Grill over medium heat, brushing with the glaze as it cooks.", 20),
            ("Rest for five minutes, then squeeze the lime over the top.", 5),
        ],
        "days": 12,
    },
    {
        "title": "Sambal Terasi",
        "category": "Sauce",
        "description": "A small, sharp bowl of chili and shrimp paste for the middle of the table.",
        "servings": 4, "prep": 10, "cook": 5, "favorite": 0,
        "cuisine": "Indonesian",
        "notes": "Make it in a stone mortar if you have one; it tastes different.",
        "tags": ["spicy"],
        "ingredients": [
            ("10", "", "chili"), ("1", "tsp", "shrimp paste"), ("2", "", "tomato"),
            ("4", "", "shallots"), ("2", "cloves", "garlic"), ("1", "", "lime"),
            ("1", "tsp", "salt"),
        ],
        "steps": [
            ("Fry the chili, tomato, shallots and garlic until softened.", 5),
            ("Grind with the shrimp paste and salt, then loosen with lime juice.", 2),
        ],
        "days": 2,
    },
    {
        "title": "Es Teh Manis",
        "category": "Drink",
        "description": "Sweet iced tea, the house drink with every meal.",
        "servings": 2, "prep": 5, "cook": 0, "favorite": 0,
        "cuisine": "Indonesian",
        "notes": "Brew it strong; the ice melts into it.",
        "tags": ["quick"],
        "ingredients": [
            ("2", "", "tea bags"), ("3", "tbsp", "sugar"), ("500", "ml", "water"),
            ("1", "", "lemon"), ("1", "cups", "ice"),
        ],
        "steps": [
            ("Brew the tea with the sugar, then cool it completely.", 0),
            ("Pour over ice and finish with a slice of lemon.", 0),
        ],
        "days": 1,
    },
]

for index, spec in enumerate(recipes):
    recipe_id = str(uuid.uuid4())
    created = now - timedelta(days=spec["days"])
    db.execute(
        """INSERT INTO Recipes (Id, Title, Description, CategoryId, Cuisine, Servings, PrepMinutes,
               CookMinutes, IsFavorite, CreatedAt, UpdatedAt, SourceDeviceId, Notes, Revision, Tags)
           VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
        (recipe_id, spec["title"], spec["description"], categories[spec["category"]], spec["cuisine"],
         spec["servings"], spec["prep"], spec["cook"], spec["favorite"],
         created.isoformat(), created.isoformat(), device, spec["notes"], 1, json.dumps(spec["tags"])),
    )
    for order, (quantity, unit, name) in enumerate(spec["ingredients"], start=1):
        db.execute(
            "INSERT INTO Ingredients (Id, RecipeId, SortOrder, Name, Quantity, Unit) VALUES (?,?,?,?,?,?)",
            (str(uuid.uuid4()), recipe_id, order, name, quantity, unit),
        )
    for order, (instruction, minutes) in enumerate(spec["steps"], start=1):
        db.execute(
            "INSERT INTO RecipeSteps (Id, RecipeId, SortOrder, Instruction, Minutes) VALUES (?,?,?,?,?)",
            (str(uuid.uuid4()), recipe_id, order, instruction, minutes),
        )

db.commit()
db.close()

settings_path = os.path.join(DEMO, "settings.json")
settings = {
    "DeviceId": "demo-device-0001",
    "DeviceName": "Rina's phone",
    "Theme": "light",
    "TextScale": 1,
    "WindowWidth": 430,
    "WindowHeight": 760,
}
open(settings_path, "w", encoding="utf-8").write(json.dumps(settings, indent=2))
print("seeded", len(recipes), "recipes into", DEMO)
