# BirdPress
Updates a post on a wordpress site with a list of recently-detected birds seen by the [BirdNet-Go](https://github.com/tphakala/birdnet-go) app. 

Example [here](https://forestgreenvillage.co.uk/birds-in-forest-green-today/) 

<img width="1152" height="920" alt="Screenshot 2026-07-07 at 08 00 09" src="https://github.com/user-attachments/assets/1ae3a1c5-8725-4923-807c-f4d1c385c56a" />

## Why?
I wanted to create a page on our village website that shares the list of identified birds in my garden, semi-live. However, rather than exposing
BirdNet-Go to the internet (which involves a potential security risk to my server) I built this tool to use a push model to write the identified
birds to a selected post on the site.

## What BirdPress Does:

Each time the app runs, it will perform several steps:

1. Query `BirdNet-Go` for the list of all detected birds
2. For each bird, check if there is a thumbnail on the Wordpress site. If not, request one from `BirdNet-Go` and upload to the WP Media library
3. Generate the HTML for the bird list, including a table of birds detected today, and a table of all other birds previously detected.
4. Update the selected Wordpress post with the new content. Note that the only content replaced will be the `[BirdPress]` placeholder. The post
   title, and any content before/after the placeholder, will be left as-is.

## Usage:
1. Set up an application password for your Wordpress site that has write-access.
    1. Go to the wordpress admin users page
    2. Select the user you wish to use
    3. At the bottom click 'Add Application Password'
    4. Enter the app name as BirdNet (or similar)
    5. Create the password and write it down somewhere

2. On the Wordpress site, create a post which will display the bird list. Add the shortCode `[BirdPress]` into the content somewhere - this will 
   be replaced with the identified bird list when BirdPress runs for the first time. Once saved (draft or published) make a note of the post ID
   in the URL.

3. Create a `BirdPressSettings.json` file for the BirdPress settings, like this.
```
{
  "birdNetUrl":  "http://192.168.1.120:8130",
  "wordpressBaseUrl" : "https://mywordpresssite.co.uk",
  "wordpressUser": "myemail@email.com",
  "wordpressPassword": "APPL ICAT IONP ASSW ORD",
  "wordpressPostId": 851,
  "minThreshold" : 0.8
}
```

4. Copy the binary for BirdPress to your server, put the `BirdPressSettings.json` file in the same folder, and then set up a CRON job to run
   the app periodically. 

5. That's it
