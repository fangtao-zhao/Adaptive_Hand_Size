# Virtual Hand Illusion
 
## To start
Run Scene： Assets-Scenes-Study 1

Update Experimental Parameters: Managers-ProcessManager-ProcessControl

## Important Parameters:
UserID // The data will be stored at "Assets\ExperimentData\UserID.csv"

Trail Time // The amount of time for the user to make a choice in each trail, i.e. the time counting down at the bottom of the page

Total Trail Number // The number of trails in the whole study 1.

Scale Changing Rate_1 // Scale changing size for each of the 0-10 trails

Number Of Rate_1 // Number of trails using Scale Changing Rate_1

Scale Changing Rate_2 // Scale changing size for each of the 11-19 trails (The last trail doesn't change the scale.)

Number Of Rate_2 // Number of trails using Scale Changing Rate_2. For two rates in total, NumberOfRate_2 = TotalTrailNumber - NumberOfRate_1. This is calculated automatically in the Start() function.

**If you change the Total Trail Number, the number and size of the Scale Changing Rate should be adjusted accordingly.*

## Operation Instructions
### Before running:
Set UserID

Use Hand Tracking System (No Controller Needed)

### During running:
There is a UI canvas in the mid of air.

User can pinch and drag the dark-colored area at the top of the UI interface with their hands.

Clink "Start" to start the fisrt trail.

In each trail, user need to fill in the bilnk: "These virtual hands are ____ than/as your real hands." using the three buttons: smaller, same, bigger.

After making a selection, the user needs to **confirm** their choice or go **back** to change it.

After clicking the **confirm** button, the screen will darken (simulating eye closure). During the black screen, the size of the virtual hand changes, and a new trail starts then.

After finishing all trails, the interface will display “**Finish\n\nThank you!**”. The experiment is over, you can end the run.

### Check Data
The data will be stored at "Assets\ExperimentData\UserID.csv"

1st column - trailNumber: Current trail count.

2nd column - currentScale: Current virtual hand scale in this trail. 1.0 is the default size of OVRHand.

3rd column - userSelection: User's selection in this trail.

**To prevent data loss, if the UserID is reused, data from multiple experiments will be recorded in the same file. Previous data will not be deleted or overwritten and can be identified by trailNumber.*
