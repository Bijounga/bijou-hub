import streamDeck from "@elgato/streamdeck";
import { TimerAction } from "./actions/timer.js";
import { HubClient } from "./hub.js";

const hub = new HubClient();
streamDeck.actions.registerAction(new TimerAction(hub));

hub.start();
streamDeck.connect();
