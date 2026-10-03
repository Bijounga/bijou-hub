import streamDeck from "@elgato/streamdeck";
import { CaptureAction } from "./actions/capture.js";
import { ExtendAction } from "./actions/extend.js";
import { GoalAction } from "./actions/goal.js";
import { PopoutAction } from "./actions/popout.js";
import { SessionAction } from "./actions/session.js";
import { TargetAction } from "./actions/target.js";
import { PomodoroAction, TimerAction } from "./actions/timer.js";
import { HubClient } from "./hub.js";

const hub = new HubClient();
streamDeck.actions.registerAction(new TimerAction(hub));
streamDeck.actions.registerAction(new SessionAction(hub));
streamDeck.actions.registerAction(new GoalAction(hub));
streamDeck.actions.registerAction(new ExtendAction(hub));
streamDeck.actions.registerAction(new PopoutAction(hub));
streamDeck.actions.registerAction(new PomodoroAction(hub));
streamDeck.actions.registerAction(new TargetAction(hub));
streamDeck.actions.registerAction(new CaptureAction(hub));

hub.start();
streamDeck.connect();
