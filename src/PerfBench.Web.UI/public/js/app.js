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
    case 'StartedEvent':
      return {
        id: action.id,
        text: action.text,
        type: action.type
      }
    case 'FinishedEvent':
      if (state.id != action.id) {
          return state
      }
      //console.log(action.id)
      return Object.assign({}, state, {
          type: action.type,
          text: action.text
      })
    default:
      return state
  }
}

const events = (state = [], action) => {
  switch (action.type) {
    case 'StartedEvent':
      return [
        ...state,
        event(undefined, action)
      ]
    case 'FinishedEvent':
      return state.map(e =>
        event(e, action)
      )
      return
    default:
      return state
  }
}

const eventsApp = combineReducers({
  events
})

function startedEvent(_id, text) {
  return {
    type: 'StartedEvent',
    id: _id,//: nextEventId++,
    text
  }
}

function finishedEvent(_id, text) {
  return {
    type: 'FinishedEvent',
    id: _id,//: nextEventId++,
    text
  }
}

const StreamingEvent = ({ type, text }) => (
  <li className={"flex-item " + type} title={text}>
  </li>
)

StreamingEvent.propTypes = {
  type: PropTypes.string.isRequired,
  //text: PropTypes.string.isRequired
}

const StreamingEventList = ({ events }) => (
  <ul className="flex-container">
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
    //text: PropTypes.string.isRequired
  }).isRequired).isRequired,
}

const Header = ({ average }) => (
  <p>
    {average}
  </p>
)

Header.propTypes = {
  average: PropTypes.number.isRequired
}

const mapStateToProps = (state) => {
  return {
    events: state.events
  }
}

const mapStateToHeaderProps = (state) => {
  let f = state.events.filter(e => e.type == "FinishedEvent")
  let finished = f.map(e => parseFloat(e.text))
  return {
    average: finished.reduce((a,b)=>a + b) / finished.length
  }
}

const VisibleHeader = connect(
  mapStateToHeaderProps
)(Header)

const VisibleEventList = connect(
  mapStateToProps
)(StreamingEventList)

const App = () => (
  <div>
    <VisibleHeader />
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
  function dispatchEvent(event) {
    switch (event.Case) {
      case 'StartedEvent':
        //console.log("Started")
        store.dispatch(startedEvent(parseInt(event.Item), ""))
        break;
      case 'FinishedEvent':
        //console.log("Finished")
        store.dispatch(finishedEvent(parseInt(event.Item1), parseFloat(event.Item2)))
        break;
      default:
        console.log("Unknown event")
        break;
    }
  }

  if (initializing) {
    initializing = false
    let data = JSON.parse(evt.data)
    let reversed = data.value.contents.reverse();
    var initialState;

    store = createStore(eventsApp)//, evt.data.value.contents)

    for (let e of reversed) {
        dispatchEvent(e[0]);
    }

    render(
      <Provider store={store}>
        <App />
      </Provider>,
      document.getElementById('react')
    )

    //store.subscribe(() =>
    //  console.log(store.getState())
    //)

  } else {
    let data = JSON.parse(evt.data)
    dispatchEvent(data.value)
    //store.dispatch(addNewEvent(JSON.stringify(data.value)))
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
