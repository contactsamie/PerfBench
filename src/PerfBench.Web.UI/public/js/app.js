import { createStore, combineReducers  } from 'redux'
import React, { PropTypes } from 'react'
import { connect, Provider  } from 'react-redux'
import { render } from 'react-dom'

let nextEventId = 0
let websocket;
let wsUri = "ws://localhost:8083/websocket";
let output;

const event = (state, action) => {
  switch (action.type) {
    case 'ADD_NEW_EVENT':
      return {
        id: action.id,
        text: action.text,
      }
    default:
      return state
  }
}

const events = (state = [], action) => {
  switch (action.type) {
    case 'ADD_NEW_EVENT':
      return [
        ...state,
        event(undefined, action)
      ]
    default:
      return state
  }
}

const eventsApp = combineReducers({
  events
})

function addNewEvent(text) {
  return {
    type: 'ADD_NEW_EVENT',
    id: nextEventId++,
    text
  }
}

const StreamingEvent = ({ text }) => (
  <li>
    {text}
  </li>
)

StreamingEvent.propTypes = {
  text: PropTypes.string.isRequired
}

const StreamingEventList = ({ events }) => (
  <ul>
    {events.map(event =>
      <StreamingEvent
        key={event.id}
        {...event}
      />
    )}
  </ul>
)

StreamingEventList.propTypes = {
    events: PropTypes.arrayOf(PropTypes.shape({
    id: PropTypes.number.isRequired,
    text: PropTypes.string.isRequired
  }).isRequired).isRequired,
}

const mapStateToProps = (state) => {
  return {
    events: state.events
  }
}

const VisibleEventList = connect(
  mapStateToProps
)(StreamingEventList)

const App = () => (
  <div>
    <VisibleEventList />
  </div>
)

var initializing = true;
var store;
function init()
{
  output = document.getElementById("output");
  testWebSocket();
}
function testWebSocket()
{
  websocket = new WebSocket(wsUri);
  websocket.onopen = function(evt) { onOpen(evt) };
  websocket.onclose = function(evt) { onClose(evt) };
  websocket.onmessage = function(evt) { onMessage(evt) };
  websocket.onerror = function(evt) { onError(evt) };
}
function onOpen(evt)
{
  writeToScreen("CONNECTED");
  doSend("INIT");
}
function onClose(evt)
{
  writeToScreen("DISCONNECTED");
}
function onMessage(evt)
{
  //writeToScreen('<span style="color: blue;">RESPONSE: ' + evt.data+'</span>');

  if (initializing) {
    initializing = false
    let data = JSON.parse(evt.data)
    store = createStore(eventsApp)//, evt.data.value.contents)

    render(
      <Provider store={store}>
        <App />
      </Provider>,
      document.getElementById('react')
    )

    store.subscribe(() =>
      console.log(store.getState())
    )

    for (let e of data.value.contents) {
      console.log(e)
      store.dispatch(addNewEvent(JSON.stringify(e)))
    }

  } else {
    let data = JSON.parse(evt.data)
    store.dispatch(addNewEvent(JSON.stringify(data.value)))
  }
  //websocket.close();
}
function onError(evt)
{
  writeToScreen('<span style="color: red;">ERROR:</span> ' + evt.data);
}
function doSend(message)
{
  writeToScreen("SENT: " + message);
  websocket.send(message);
}
function writeToScreen(message)
{
  var pre = document.createElement("p");
  pre.style.wordWrap = "break-word";
  pre.innerHTML = message;
  output.appendChild(pre);
}
window.addEventListener("load", init, false);
