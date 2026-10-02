import streamDeck from "@elgato/streamdeck";
import { ExtendAction } from "./actions/extend.js";
import { GoalAction } from "./actions/goal.js";
import { SessionAction } from "./actions/session.js";
import { TimerAction } from "./actions/timer.js";
import { HubClient } from "./hub.js";

const hub = new HubClient();
streamDeck.actions.registerAction(new TimerAction(hub));
streamDeck.actions.registerAction(new SessionAction(hub));
streamDeck.actions.registerAction(new GoalAction(hub));
streamDeck.actions.registerAction(new ExtendAction(hub));

hub.start();
streamDeck.connect();
